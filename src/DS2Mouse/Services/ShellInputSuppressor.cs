using System.Runtime.InteropServices;

namespace DS2Mouse.Services;

/// <summary>
/// Suppresses Windows 11's XAML gamepad navigation in shell apps (Settings,
/// Start menu, etc.) by swallowing the synthetic keyboard events Windows
/// injects from gamepad input.
///
/// Win11 translates gamepad D-pad / sticks / face buttons into virtual key
/// presses (arrows, Tab, Enter, Esc, Space) and feeds them into the global
/// input queue marked with the LLKHF_INJECTED flag. A WH_KEYBOARD_LL hook
/// sees these before XAML focus navigation does, so dropping them stops the
/// double-input behavior.
///
/// Our own synthetic keystrokes (from mapping buttons to keys) also carry
/// LLKHF_INJECTED, so we mark every SendInput call with
/// <see cref="InputSimulator.SyntheticTag"/> in dwExtraInfo and let those
/// through.
/// </summary>
public static class ShellInputSuppressor
{
    private const int  WH_KEYBOARD_LL  = 13;
    private const int  HC_ACTION       = 0;
    private const uint LLKHF_INJECTED  = 0x00000010;

    private static IntPtr _hook;
    private static LowLevelKeyboardProc? _proc;  // GC pin

    public static bool Enabled { get; private set; }

    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
        Enabled = _hook != IntPtr.Zero;
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
        Enabled = false;
    }

    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION && Enabled)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // Drop only OS-injected keys that aren't ours. Real keystrokes
            // (LLKHF_INJECTED clear) and our own SendInput keys (tagged) flow
            // through untouched.
            if ((kb.flags & LLKHF_INJECTED) != 0 && kb.dwExtraInfo != InputSimulator.SyntheticTag)
            {
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    // ----- P/Invoke -----

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint   vkCode;
        public uint   scanCode;
        public uint   flags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
