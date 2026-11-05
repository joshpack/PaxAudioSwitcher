using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PaxAudioSwitcher.Audio;
using PaxAudioSwitcher.Platform;
using PaxAudioSwitcher.Services;
using PaxAudioSwitcher.Views;

namespace PaxAudioSwitcher;

/// <summary>
/// --self-test DIR: checks the parts that need real Windows audio without changing anything the user would notice
/// (the current defaults are re-applied, not switched) and renders the settings window to a PNG without showing it.
/// </summary>
internal static class SelfTest
{
    public static string? Dir { get; set; }

    public static void Audio(StringBuilder r)
    {
        using var sw = new Switcher(interactive: false);
        foreach (var flow in new[] { EDataFlow.Render, EDataFlow.Capture })
        {
            var current = sw.Audio.GetDefault(flow);
            r.AppendLine($"{flow} default: {current?.FullName ?? "none"}");
            foreach (var d in sw.Audio.List(flow))
                r.AppendLine($"  - {d.FullName}  [{d.Id}]");
            r.AppendLine($"  cycle list: {sw.CycleList(flow).Count} device(s)");
            if (current is not null)
            {
                var comms = sw.Audio.GetDefault(flow, ERole.Communications)?.Id == current.Id;
                r.AppendLine($"  re-apply current default (IPolicyConfig): {(sw.Audio.SetDefault(current.Id, comms) ? "OK" : "FAILED")}");
                r.AppendLine($"  default unchanged afterwards: {sw.Audio.GetDefault(flow)?.Id == current.Id}");
            }
        }

        r.AppendLine($"Mic muted: {sw.Audio.IsMicMuted()?.ToString() ?? "no mic"}");

        // Apps given their own device in the Volume mixer (read only: nothing is changed here).
        var pinned = new SortedSet<string>();
        var scan = System.Diagnostics.Stopwatch.StartNew();
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            using (proc)
            {
                if (AppRouting.OwnDevice((uint)proc.Id, EDataFlow.Render) is { } own)
                    pinned.Add($"{proc.ProcessName} -> {own}");
            }
        }

        r.AppendLine($"Running apps with their own output device: {pinned.Count} (scan took {scan.ElapsedMilliseconds} ms)");
        foreach (var p in pinned)
            r.AppendLine("  - " + p);

        using var hk = new HotkeyManager();
        var probe = new Hotkey(Hotkey.Ctrl | Hotkey.Alt | Hotkey.Shift | Hotkey.Win, 0x87);   // F24: nobody uses it
        r.AppendLine($"Hotkey register (Ctrl+Alt+Shift+Win+F24): {(hk.Register(probe, () => { }) ? "OK" : "FAILED")}");
        hk.UnregisterAll();
        foreach (var (name, key) in new[] { ("playback", sw.Settings.PlaybackHotkey), ("recording", sw.Settings.RecordingHotkey), ("mute", sw.Settings.MuteHotkey) })
            if (key is not null)
                r.AppendLine($"Hotkey {name} {key}: {(hk.Register(key, () => { }) ? "free" : "TAKEN by another program")}");
        hk.UnregisterAll();
    }

    public static void Window(StringBuilder r, Switcher sw)
    {
        // A throw-away profile so the profile card gets built too; never saved.
        sw.Settings.Profiles.Add(new Profile { Name = "Self-test", PlaybackId = sw.Audio.GetDefault(EDataFlow.Render)?.Id, Apps = { "game.exe" } });
        var window = new SettingsWindow(sw, () => { })
        {
            // Far off every screen, never activated, not on the taskbar: nothing appears for the user.
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual,
            Position = new PixelPoint(-30000, -30000),
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        foreach (var tab in new[] { 0, 1, 2 })
        {
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = tab;
            var size = new Size(560, 740);
            window.Width = size.Width;
            window.Height = size.Height;
            window.UpdateLayout();
            using var bmp = new RenderTargetBitmap(new PixelSize(560, 740));
            bmp.Render(window);
            bmp.Save(Path.Combine(Dir!, $"tab{tab}.png"));
        }

        window.Close();
        sw.Settings.Profiles.RemoveAt(sw.Settings.Profiles.Count - 1);
        r.AppendLine("Settings window: built and rendered");
    }
}
