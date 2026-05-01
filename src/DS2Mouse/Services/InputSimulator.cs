using System.Runtime.InteropServices;

namespace DS2Mouse.Services;

public enum MouseButton { Left, Right, Middle, X1, X2 }

/// <summary>Win32 SendInput wrapper for synthetic mouse and keyboard events.</summary>
public static class InputSimulator
{
    public static void MoveRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_MOVE,
                }
            }
        };
        Send(input);
    }

    public static void MouseDown(MouseButton btn)
    {
        var (down, _, data) = ButtonFlags(btn);
        Send(MouseInput(down, data));
    }

    public static void MouseUp(MouseButton btn)
    {
        var (_, up, data) = ButtonFlags(btn);
        Send(MouseInput(up, data));
    }

    public static void MouseClick(MouseButton btn)
    {
        var (down, up, data) = ButtonFlags(btn);
        Span<INPUT> two = stackalloc INPUT[2];
        two[0] = MouseInput(down, data);
        two[1] = MouseInput(up, data);
        SendBatch(two);
    }

    /// <summary>Wheel delta in WHEEL_DELTA units (120 = one notch).</summary>
    public static void ScrollVertical(int delta)
    {
        if (delta == 0) return;
        var i = MouseInput(MOUSEEVENTF_WHEEL, (uint)delta);
        Send(i);
    }

    public static void ScrollHorizontal(int delta)
    {
        if (delta == 0) return;
        var i = MouseInput(MOUSEEVENTF_HWHEEL, (uint)delta);
        Send(i);
    }

    public static void KeyDown(ushort vk)
    {
        Send(KeyInput(vk, KEYEVENTF_NONE));
    }

    public static void KeyUp(ushort vk)
    {
        Send(KeyInput(vk, KEYEVENTF_KEYUP));
    }

    public static void KeyTap(ushort vk)
    {
        Span<INPUT> two = stackalloc INPUT[2];
        two[0] = KeyInput(vk, KEYEVENTF_NONE);
        two[1] = KeyInput(vk, KEYEVENTF_KEYUP);
        SendBatch(two);
    }

    // ----- internals -----

    private static (uint down, uint up, uint data) ButtonFlags(MouseButton b) => b switch
    {
        MouseButton.Left   => (MOUSEEVENTF_LEFTDOWN,   MOUSEEVENTF_LEFTUP,   0),
        MouseButton.Right  => (MOUSEEVENTF_RIGHTDOWN,  MOUSEEVENTF_RIGHTUP,  0),
        MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0),
        MouseButton.X1     => (MOUSEEVENTF_XDOWN,      MOUSEEVENTF_XUP,      XBUTTON1),
        MouseButton.X2     => (MOUSEEVENTF_XDOWN,      MOUSEEVENTF_XUP,      XBUTTON2),
        _ => (0u, 0u, 0u),
    };

    private static INPUT MouseInput(uint flags, uint data) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dwFlags = flags,
                mouseData = data,
            }
        }
    };

    private static INPUT KeyInput(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = flags,
            }
        }
    };

    private static unsafe void Send(INPUT input)
    {
        var i = input;
        SendInput(1, &i, sizeof(INPUT));
    }

    private static unsafe void SendBatch(Span<INPUT> inputs)
    {
        if (inputs.Length == 0) return;
        fixed (INPUT* p = inputs)
        {
            SendInput((uint)inputs.Length, p, sizeof(INPUT));
        }
    }

    // ----- P/Invoke -----

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE       = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    private const uint MOUSEEVENTF_XDOWN      = 0x0080;
    private const uint MOUSEEVENTF_XUP        = 0x0100;
    private const uint MOUSEEVENTF_WHEEL      = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL     = 0x01000;

    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    private const uint KEYEVENTF_NONE  = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern unsafe uint SendInput(uint nInputs, INPUT* pInputs, int cbSize);
}
