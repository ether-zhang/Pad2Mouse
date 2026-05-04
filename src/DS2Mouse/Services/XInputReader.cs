using System.Diagnostics;
using System.Runtime.InteropServices;
using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Polls XInput user slots 0..3 at 125 Hz and translates the first connected
/// gamepad into <see cref="DualSenseState"/>. This covers Xbox 360 (which
/// does not expose standard HID gamepad), Xbox One, and Xbox Series wired
/// or via the Xbox Wireless Adapter / Bluetooth.
/// </summary>
public sealed class XInputReader : IControllerReader
{
    private const int PollIntervalMs = 8;
    private const int ErrorSuccess = 0;
    private const int ErrorDeviceNotConnected = 1167;

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private uint _activeSlot = uint.MaxValue;

    public ConnectionType ConnectionType { get; private set; } = ConnectionType.Disconnected;
    public string? DeviceName { get; private set; }
    public ControllerKind Kind => ControllerKind.Xbox;
    public DualSenseState? LatestState { get; private set; }

    public event Action<ConnectionType>? ConnectionChanged;
    public event Action<DualSenseState>? FrameReceived;
#pragma warning disable CS0067 // never fires — kind is fixed for this reader
    public event Action<ControllerKind>? KindChanged;
#pragma warning restore CS0067

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
        SetConnection(ConnectionType.Disconnected);
        DeviceName = null;
        _activeSlot = uint.MaxValue;
    }

    public void Dispose() => Stop();

    private void PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // If we already have an active slot, poll it directly. If it
                // disconnects, fall through to the scan.
                if (_activeSlot != uint.MaxValue)
                {
                    var rc = XInputGetState(_activeSlot, out var st);
                    if (rc == ErrorSuccess)
                    {
                        EmitFrame(st);
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }
                    if (rc == ErrorDeviceNotConnected)
                    {
                        _activeSlot = uint.MaxValue;
                        DeviceName = null;
                        SetConnection(ConnectionType.Disconnected);
                    }
                }

                // No active slot — scan all four for a connected gamepad.
                for (uint i = 0; i < 4; i++)
                {
                    if (XInputGetState(i, out var st) == ErrorSuccess)
                    {
                        _activeSlot = i;
                        DeviceName = "Xbox Controller";
                        SetConnection(ConnectionType.Usb);
                        EmitFrame(st);
                        break;
                    }
                }
            }
            catch
            {
                // never let an exception kill the poll thread
            }
            Thread.Sleep(PollIntervalMs);
        }
    }

    private void EmitFrame(XINPUT_STATE st)
    {
        var g = st.Gamepad;
        var state = new DualSenseState(
            LeftStickX:  NormStick(g.sThumbLX),
            LeftStickY:  NormStick(g.sThumbLY),  // XInput Y is already +up
            RightStickX: NormStick(g.sThumbRX),
            RightStickY: NormStick(g.sThumbRY),
            L2Trigger:   g.bLeftTrigger  / 255f,
            R2Trigger:   g.bRightTrigger / 255f,
            Buttons:     TranslateButtons(g.wButtons, g.bLeftTrigger, g.bRightTrigger),
            TimestampTicks: Stopwatch.GetTimestamp());

        LatestState = state;
        FrameReceived?.Invoke(state);
    }

    private static float NormStick(short raw)
    {
        // Map signed 16-bit to -1..1. Negative range is one wider so clamp at -1.
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
        // Mapper applies its own threshold to L2/R2 floats; mirror the bool flag
        // for parity with DualSense's button bit so trigger-as-button paths
        // (which read DualSenseButton.L2/R2) continue to work.
        const byte TriggerBit = 30;
        if (lt > TriggerBit) f |= DualSenseButton.L2;
        if (rt > TriggerBit) f |= DualSenseButton.R2;
        return f;
    }

    private void SetConnection(ConnectionType c)
    {
        if (ConnectionType == c) return;
        ConnectionType = c;
        ConnectionChanged?.Invoke(c);
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
