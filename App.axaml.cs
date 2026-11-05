using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PaxAudioSwitcher.Audio;
using PaxAudioSwitcher.Platform;
using PaxAudioSwitcher.Services;
using PaxAudioSwitcher.Views;

namespace PaxAudioSwitcher;

public partial class App : Application
{
    private Switcher _sw = null!;
    private SettingsWindow? _window;
    private TrayIcon _tray = null!;
    private IClassicDesktopStyleApplicationLifetime _lifetime = null!;
    private WindowIcon? _icon, _mutedIcon;
    private bool? _shownMuted;
    private readonly CancellationTokenSource _quit = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
            if (SelfTest.Dir is { } dir)
            {
                lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var report = new System.Text.StringBuilder();
                try
                {
                    SelfTest.Audio(report);
                    using var sw = new Switcher(interactive: false);
                    SelfTest.Window(report, sw);
                }
                catch (Exception ex)
                {
                    report.AppendLine("CRASH: " + ex);
                }

                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
                Dispatcher.UIThread.Post(() => lifetime.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // The tray keeps the app (and the hotkeys) alive with no window open.
            lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _sw = new Switcher(interactive: true);
            _sw.Notice += n => Banner.Show(n, _sw.Settings);
            _sw.DevicesChanged += UpdateTray;
            _sw.Start(a => Dispatcher.UIThread.Post(a));

            _icon = AppIcon.Create();
            _mutedIcon = AppIcon.Create(muted: true);
            _tray = new TrayIcon { Icon = _icon, ToolTipText = "PAX Audio Switcher" };
            _tray.Clicked += (_, _) => OpenSettings();
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
            UpdateTray();

            _ = Task.Run(ListenForCommands);

            // Started by hand: show the settings, unless the user chose to start in the tray. Started at sign-in
            // (--minimized): stay quietly in the tray. Either way a blocked hotkey needs the user's attention.
            var inTray = Program.StartMinimized || _sw.Settings.StartInTray;
            if (!inTray || (_sw.HotkeyConflicts.Count > 0 && !_sw.Settings.StartInTray))
                OpenSettings();
            else
            {
                if (_sw.HotkeyConflicts.Count > 0)
                    // Asked to stay in the tray: say so in a banner instead of opening the window.
                    Banner.Show(new SwitchNotice("HOTKEY BLOCKED", string.Join("\n", _sw.HotkeyConflicts) + "\nClick the tray icon to pick another", Warning: true), _sw.Settings, force: true);
                DispatcherTimer.RunOnce(MemoryTrim.Trim, TimeSpan.FromSeconds(3));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Rebuilds the tray menu: every device to pick directly, the profiles, mute, settings and quit.</summary>
    private void UpdateTray()
    {
        var menu = new NativeMenu();
        var playing = _sw.Audio.GetDefault(EDataFlow.Render);
        var recording = _sw.Audio.GetDefault(EDataFlow.Capture);

        menu.Add(DeviceMenu("Playback", EDataFlow.Render, playing?.Id));
        menu.Add(DeviceMenu("Recording", EDataFlow.Capture, recording?.Id));

        if (_sw.Settings.Profiles.Count > 0)
        {
            var profiles = new NativeMenu();
            foreach (var p in _sw.Settings.Profiles)
            {
                var profile = p;
                var item = new NativeMenuItem(p.Name);
                item.Click += (_, _) => _sw.ApplyProfile(profile);
                profiles.Add(item);
            }

            menu.Add(new NativeMenuItem("Profiles") { Menu = profiles });
        }

        var muted = _sw.Audio.IsMicMuted();
        var mute = new NativeMenuItem("Mute microphone") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = muted == true, IsEnabled = muted is not null };
        mute.Click += (_, _) => _sw.ToggleMute();
        menu.Add(mute);

        menu.Add(new NativeMenuItemSeparator());
        var open = new NativeMenuItem("Settings…");
        open.Click += (_, _) => OpenSettings();
        menu.Add(open);
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        menu.Add(quit);
        _tray.Menu = menu;

        _tray.ToolTipText = Trim($"🔊 {playing?.FullName ?? "No playback device"}\n🎙 {recording?.FullName ?? "No microphone"}{(muted == true ? " (muted)" : "")}");
        if (muted == true != _shownMuted)
        {
            _shownMuted = muted == true;
            _tray.Icon = _shownMuted == true ? _mutedIcon : _icon;
        }

        _window?.Refresh();
    }

    private NativeMenuItem DeviceMenu(string title, EDataFlow flow, string? currentId)
    {
        var sub = new NativeMenu();
        var devices = _sw.Audio.List(flow);
        foreach (var d in devices)
        {
            var device = d;
            var item = new NativeMenuItem(d.FullName) { ToggleType = MenuItemToggleType.Radio, IsChecked = d.Id == currentId };
            item.Click += (_, _) => _sw.SwitchTo(device);
            sub.Add(item);
        }

        if (devices.Count == 0)
            sub.Add(new NativeMenuItem("Nothing plugged in") { IsEnabled = false });
        return new NativeMenuItem(title) { Menu = sub };
    }

    /// <summary>Windows cuts tray tooltips off at 127 characters.</summary>
    private static string Trim(string s) => s.Length <= 127 ? s : s[..126] + "…";

    /// <summary>Commands from a second launch (a shortcut, a script, or double-clicking the app again).</summary>
    private async Task ListenForCommands()
    {
        while (!_quit.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_quit.Token);
                using var reader = new StreamReader(pipe);
                var args = (await reader.ReadToEndAsync(_quit.Token)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Dispatcher.UIThread.Post(() =>
                {
                    if (Commands.HasAction(args))
                        Commands.Run(_sw, args);
                    else if (args.Contains("--show", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
                        OpenSettings();
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    /// <summary>Opens the settings window, creating it only now; closing it frees it again.</summary>
    private void OpenSettings()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new SettingsWindow(_sw, UpdateTray);
        _window.Closed += (_, _) =>
        {
            _window = null;
            // Hand the memory the window used back to the system; the tray needs almost none.
            DispatcherTimer.RunOnce(MemoryTrim.Trim, TimeSpan.FromSeconds(1));
        };
        _window.Show();
        _window.Activate();
    }

    private void Quit()
    {
        _quit.Cancel();
        _window?.Close();
        _sw.Dispose();
        _tray.IsVisible = false;
        _tray.Dispose();
        _lifetime.Shutdown();
    }
}
