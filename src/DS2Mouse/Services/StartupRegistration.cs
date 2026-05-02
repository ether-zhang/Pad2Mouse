using System.IO;
using Microsoft.Win32;

namespace DS2Mouse.Services;

/// <summary>
/// Registers / unregisters the EXE under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run so Windows launches
/// it on user login. HKCU keeps it per-user, no admin required.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Pad2Mouse";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(existing)) return false;
            // Treat the entry as stale if it points at a now-missing exe.
            var path = StripQuotes(existing);
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            // Quote the path so spaces in the install dir don't truncate the command line.
            key?.SetValue(ValueName, $"\"{exe}\"");
        }
        catch
        {
            // Autostart is a convenience, not critical — never crash the app.
        }
    }

    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Same — never crash on registry-side problems.
        }
    }

    private static string StripQuotes(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
}
