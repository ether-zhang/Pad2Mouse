using System.Runtime.InteropServices;

namespace DS2Mouse.Services;

/// <summary>
/// Polls Shell32's user-notification state to detect D3D-exclusive fullscreen
/// or presentation mode. When detected and the foreground process is NOT in
/// the whitelist, suppresses input mapping.
/// </summary>
public sealed class FullscreenGuard : IDisposable
{
    private readonly Timer _timer;
    private readonly Func<IReadOnlyList<string>> _whitelistAccessor;

    public FullscreenGuard(Func<IReadOnlyList<string>> whitelistAccessor)
    {
        _whitelistAccessor = whitelistAccessor;
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsFullscreen { get; private set; }
    public string? ForegroundName { get; private set; }
    public bool Suppressed { get; private set; }

    public event Action? StateChanged;

    public void Start() => _timer.Change(0, 500);
    public void Stop()  => _timer.Change(Timeout.Infinite, Timeout.Infinite);
    public void Dispose() => _timer.Dispose();

    public bool ShouldSuppress() => Suppressed;

    private void Poll()
    {
        try
        {
            bool wasSuppressed = Suppressed;
            string? prevName = ForegroundName;
            bool prevFs = IsFullscreen;

            IsFullscreen = QueryFullscreen();
            ForegroundName = ForegroundProcess.Name();

            if (!IsFullscreen)
            {
                Suppressed = false;
            }
            else
            {
                var list = _whitelistAccessor();
                Suppressed = !ProcessInList(ForegroundName, list);
            }

            if (Suppressed != wasSuppressed
                || ForegroundName != prevName
                || IsFullscreen != prevFs)
            {
                StateChanged?.Invoke();
            }
        }
        catch
        {
            // Polling must never throw to caller.
        }
    }

    private static bool ProcessInList(string? processName, IReadOnlyList<string> list)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var entry = NormalizeName(list[i]);
            if (string.Equals(entry, processName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeName(string s) =>
        s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;

    private static bool QueryFullscreen()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state == QueryUserNotificationState.RunningD3DFullScreen
            || state == QueryUserNotificationState.PresentationMode;
    }

    private enum QueryUserNotificationState
    {
        NotPresent              = 1,
        Busy                    = 2,
        RunningD3DFullScreen    = 3,
        PresentationMode        = 4,
        AcceptsNotifications    = 5,
        QuietTime               = 6,
        App                     = 7,
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);
}
