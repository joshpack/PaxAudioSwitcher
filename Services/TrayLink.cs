using PaxStandard;

namespace PaxAudioSwitcher.Services;

/// <summary>
/// The PAX Toolkit link for the tray icon: %APPDATA%\PAX\toolkit.json says whether it shows. The file is watched, so
/// turning the icon off in PAX Toolkit (or in this app's settings, which writes the same file) hides it at once.
/// </summary>
public sealed class TrayLink : IDisposable
{
    public const string AppId = "PaxAudioSwitcher";

    private readonly Action<bool> _apply;
    private readonly Action<Action> _post;
    private readonly IDisposable _watch;
    private bool? _applied;

    /// <param name="apply">Shows or hides the icon; called through <paramref name="post"/> (the UI thread).</param>
    public TrayLink(Action<bool> apply, Action<Action> post)
    {
        _apply = apply;
        _post = post;
        Update();
        _watch = PaxLink.Watch(() => _post(Update));
    }

    public bool Visible => _applied ?? true;

    /// <summary>Raised after the icon was shown or hidden, whichever side changed it.</summary>
    public event Action<bool>? Changed;

    /// <summary>The "Show tray icon" switch: writes toolkit.json (so PAX Toolkit agrees) and applies it now.</summary>
    public void Set(bool visible)
    {
        PaxLink.SetTrayIconVisible(AppId, visible);
        Update();
    }

    private void Update()
    {
        var visible = PaxLink.TrayIconVisible(AppId);
        if (visible == _applied)
            return;
        _applied = visible;
        _apply(visible);
        Changed?.Invoke(visible);
    }

    public void Dispose() => _watch.Dispose();
}
