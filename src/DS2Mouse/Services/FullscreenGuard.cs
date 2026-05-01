using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DS2Mouse.Services;

/// <summary>
/// Detects whether the current foreground window covers an entire monitor
/// (catches both D3D exclusive AND borderless-window fullscreen). When the
/// foreground process is NOT in the whitelist, suppresses input mapping.
/// SHQueryUserNotificationState is kept as a complementary signal so that
/// PowerPoint-style presentation-mode and edge-case D3D modes are still
/// covered even if the rect probe misses.
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

            ProbeForeground(out var fs, out var name);
            IsFullscreen = fs;
            ForegroundName = name;

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

    private static void ProbeForeground(out bool fullscreen, out string? processName)
    {
        fullscreen = false;
        processName = null;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        // Skip shell surfaces — empty desktop / taskbar / wallpaper layer
        // would otherwise trigger the rect probe since they cover the screen.
        var cls = GetWindowClassName(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return;

        processName = ProcessNameFromHwnd(hwnd);

        // Skip our own window so opening Pad2Mouse maximized doesn't self-suppress.
        if (string.Equals(processName, "Pad2Mouse", StringComparison.OrdinalIgnoreCase))
            return;

        fullscreen = IsHwndCoveringMonitor(hwnd) || QuerySystemFullscreenHint();
    }

    private static bool IsHwndCoveringMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var win)) return false;

        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMon == IntPtr.Zero) return false;

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return false;

        var mon = mi.rcMonitor;
        // Exact match catches both D3D exclusive (window rect == monitor) and
        // borderless fullscreen. Maximized windows have rect == work area
        // (taskbar excluded), so they don't match unless the taskbar is
        // auto-hidden — which is borderline-fullscreen anyway.
        return win.Left == mon.Left
            && win.Top == mon.Top
            && win.Right == mon.Right
            && win.Bottom == mon.Bottom;
    }

    private static bool QuerySystemFullscreenHint()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state == QueryUserNotificationState.RunningD3DFullScreen
            || state == QueryUserNotificationState.PresentationMode;
    }

    private static string? ProcessNameFromHwnd(IntPtr hwnd)
    {
        if (GetWindowThreadProcessId(hwnd, out var pid) == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }
    }

    private static string? GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(128);
        var n = GetClassName(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : null;
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

    // ----- Win32 -----

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);
}
