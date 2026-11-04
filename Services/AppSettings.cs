using System.Text.Json;
using PaxAudioSwitcher.Platform;

namespace PaxAudioSwitcher.Services;

public enum NotifyMode { Banner, BannerAndSound, Sound, Off }

/// <summary>A saved pair of devices, switched to by its own hotkey or when one of its programs comes to the front.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "New profile";

    /// <summary>Device ids; null leaves that side as it is.</summary>
    public string? PlaybackId { get; set; }
    public string? RecordingId { get; set; }

    public Hotkey? Hotkey { get; set; }

    /// <summary>Exe names (e.g. "discord.exe") that switch to this profile when their window comes to the front.</summary>
    public List<string> Apps { get; set; } = new();

    /// <summary>Put the previous devices back once every one of those programs has closed.</summary>
    public bool RestoreOnClose { get; set; } = true;
}

/// <summary>Everything the user can change, saved as JSON in the per-user app-data folder.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Hotkey? PlaybackHotkey { get; set; } = new(Hotkey.Ctrl | Hotkey.Alt, 0x7A);   // F11
    public Hotkey? RecordingHotkey { get; set; } = new(Hotkey.Ctrl | Hotkey.Alt, 0x76);  // F7
    public Hotkey? MuteHotkey { get; set; } = new(Hotkey.Ctrl | Hotkey.Alt, 0x4D);       // M

    /// <summary>Device ids the hotkeys cycle through. Empty means every device that is plugged in.</summary>
    public List<string> PlaybackCycle { get; set; } = new();
    public List<string> RecordingCycle { get; set; } = new();

    /// <summary>Also make the new device the one calls (Discord, Teams…) use. Off keeps a fixed headset for calls.</summary>
    public bool SetCommunications { get; set; } = true;

    public NotifyMode Notify { get; set; } = NotifyMode.Banner;

    public List<Profile> Profiles { get; set; } = new();

    /// <summary>Last known name of every device seen, so unplugged devices in a profile still have a name.</summary>
    public Dictionary<string, string> KnownNames { get; set; } = new();

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PaxAudioSwitcher");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            /* a damaged file just means defaults */
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            /* best effort */
        }
    }

    public string NameOf(string? id) =>
        id is null ? "Leave as it is" : KnownNames.TryGetValue(id, out var n) ? n : "Unknown device";
}
