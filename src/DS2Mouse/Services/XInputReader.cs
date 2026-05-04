using System.Diagnostics;
using System.Runtime.InteropServices;
using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Polls every XInput user slot (0..3) at 125 Hz and merges all connected
/// gamepads into a single <see cref="DualSenseState"/>. Inputs from multiple
/// pads combine — any pad pressing a button is enough; sticks take the
/// larger-magnitude of the two. Covers Xbox 360 (no standard HID gamepad),
/// Xbox One, and Xbox Series via wired / Wireless Adapter / Bluetooth.
/// </summary>
public sealed class XInputReader : IControllerReader
{
    private const int PollIntervalMs = 8;
    private const int ErrorSuccess = 0;
    private const int ErrorDeviceNotConnected = 1167;
    private const string ProductName = "Xbox Controller";

    private CancellationTokenSource? _cts;
    private Thread? _thread;

    // One entry per XInput user slot. Null = currently disconnected.
    private readonly DualSenseState?[] _slotStates = new DualSenseState?[4];
    private readonly bool[] _slotConnected = new bool[4];

    public ConnectionType ConnectionType { get; private set; } = ConnectionType.Disconnected;
    public string? DeviceName { get; private set; }
    public ControllerKind Kind { get; private set; } = ControllerKind.None;
    public DualSenseState? LatestState { get; private set; }

    public event Action<ConnectionType>? ConnectionChanged;
    public event Action<DualSenseState>? FrameReceived;
    public event Action<ControllerKind>? KindChanged;

    public void Start()
    {
        if (_thread != null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => PollLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "XInputReader",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(500);
        _thread = null;
        _cts?.Dispose();
        _cts = null;
        for (int i = 0; i < 4; i++) { _slotStates[i] = null; _slotConnected[i] = false; }
        LatestState = null;
        UpdateAggregates();
    }

    public void Dispose() => Stop();

    private void PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DualSenseState? merged = null;
                for (uint i = 0; i < 4; i++)
                {
                    int rc = XInputGetState(i, out var st);
                    if (rc == ErrorSuccess)
                    {
                        var state = Convert(st);
                        _slotStates[i] = state;
                        _slotConnected[i] = true;
                        merged = merged is null ? state : StateMerge.Combine(merged.Value, state);
                        FrameReceived?.Invoke(state);
                    }
                    else
                    {
                        _slotStates[i] = null;
                        _slotConnected[i] = false;
                    }
                }

                LatestState = merged;
                UpdateAggregates();
            }
            catch
            {
                // never let an exception kill the poll thread
            }
            try { Thread.Sleep(PollIntervalMs); } catch { }
        }
    }

    private void UpdateAggregates()
    {
        int count = 0;
        for (int i = 0; i < 4; i++) if (_slotConnected[i]) count++;

        string? newName = count switch
        {
            0 => null,
            1 => ProductName,
            _ => BuildIndexedName(count),
        };
        var newConn = count > 0 ? ConnectionType.Usb : ConnectionType.Disconnected;
        var newKind = count > 0 ? ControllerKind.Xbox : ControllerKind.None;

        bool nameChanged = DeviceName != newName;
        bool connChanged = ConnectionType != newConn;
        bool kindChanged = Kind != newKind;

        DeviceName = newName;
        ConnectionType = newConn;
        Kind = newKind;

        if (connChanged || nameChanged) ConnectionChanged?.Invoke(newConn);
        if (kindChanged) KindChanged?.Invoke(newKind);
    }

    private string BuildIndexedName(int count)
    {
        var parts = new string[count];
        int n = 0;
        for (int i = 0; i < 4; i++)
        {
            if (_slotConnected[i])
            {
                parts[n] = $"{ProductName}-{n + 1}";
                n++;
            }
        }
        return string.Join(" + ", parts);
    }

    private static DualSenseState Convert(XINPUT_STATE st)
    {
        var g = st.Gamepad;
        return new DualSenseState(
            LeftStickX:  NormStick(g.sThumbLX),
            LeftStickY:  NormStick(g.sThumbLY),  // XInput Y is already +up
            RightStickX: NormStick(g.sThumbRX),
            RightStickY: NormStick(g.sThumbRY),
            L2Trigger:   g.bLeftTrigger  / 255f,
            R2Trigger:   g.bRightTrigger / 255f,
            Buttons:     TranslateButtons(g.wButtons, g.bLeftTrigger, g.bRightTrigger),
            TimestampTicks: Stopwatch.GetTimestamp());
    }

    private static float NormStick(short raw)
    {
        var v = raw / 32767f;
        return v < -1f ? -1f : v > 1f ? 1f : v;
    }

    private static DualSenseButton TranslateButtons(ushort xb, byte lt, byte rt)
    {
        DualSenseButton f = DualSenseButton.None;
        if ((xb & XINPUT_GAMEPAD_DPAD_UP)        != 0) f |= DualSenseButton.DPadUp;
        if ((xb & XINPUT_GAMEPAD_DPAD_DOWN)      != 0) f |= DualSenseButton.DPadDown;
        if ((xb & XINPUT_GAMEPAD_DPAD_LEFT)      != 0) f |= DualSenseButton.DPadLeft;
        if ((xb & XINPUT_GAMEPAD_DPAD_RIGHT)     != 0) f |= DualSenseButton.DPadRight;
        if ((xb & XINPUT_GAMEPAD_START)          != 0) f |= DualSenseButton.Options;
        if ((xb & XINPUT_GAMEPAD_BACK)           != 0) f |= DualSenseButton.Share;
        if ((xb & XINPUT_GAMEPAD_LEFT_THUMB)     != 0) f |= DualSenseButton.L3;
        if ((xb & XINPUT_GAMEPAD_RIGHT_THUMB)    != 0) f |= DualSenseButton.R3;
        if ((xb & XINPUT_GAMEPAD_LEFT_SHOULDER)  != 0) f |= DualSenseButton.L1;
        if ((xb & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) f |= DualSenseButton.R1;
        if ((xb & XINPUT_GAMEPAD_A)              != 0) f |= DualSenseButton.Cross;
        if ((xb & XINPUT_GAMEPAD_B)              != 0) f |= DualSenseButton.Circle;
        if ((xb & XINPUT_GAMEPAD_X)              != 0) f |= DualSenseButton.Square;
        if ((xb & XINPUT_GAMEPAD_Y)              != 0) f |= DualSenseButton.Triangle;
        const byte TriggerBit = 30;
        if (lt > TriggerBit) f |= DualSenseButton.L2;
        if (rt > TriggerBit) f |= DualSenseButton.R2;
        return f;
    }

    // ----- P/Invoke -----

    private const ushort XINPUT_GAMEPAD_DPAD_UP        = 0x0001;
    private const ushort XINPUT_GAMEPAD_DPAD_DOWN      = 0x0002;
    private const ushort XINPUT_GAMEPAD_DPAD_LEFT      = 0x0004;
    private const ushort XINPUT_GAMEPAD_DPAD_RIGHT     = 0x0008;
    private const ushort XINPUT_GAMEPAD_START          = 0x0010;
    private const ushort XINPUT_GAMEPAD_BACK           = 0x0020;
    private const ushort XINPUT_GAMEPAD_LEFT_THUMB     = 0x0040;
    private const ushort XINPUT_GAMEPAD_RIGHT_THUMB    = 0x0080;
    private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER  = 0x0100;
    private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    private const ushort XINPUT_GAMEPAD_A              = 0x1000;
    private const ushort XINPUT_GAMEPAD_B              = 0x2000;
    private const ushort XINPUT_GAMEPAD_X              = 0x4000;
    private const ushort XINPUT_GAMEPAD_Y              = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
}
