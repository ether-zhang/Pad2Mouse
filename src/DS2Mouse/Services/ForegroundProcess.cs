using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DS2Mouse.Services;

public static class ForegroundProcess
{
    /// <summary>Returns the foreground window's process name without ".exe", or null.</summary>
    public static string? Name()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(hwnd, out var pid) == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
