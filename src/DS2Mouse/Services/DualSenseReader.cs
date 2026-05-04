using System.Diagnostics;
using DS2Mouse.Models;
using HidSharp;

namespace DS2Mouse.Services;

public sealed class DualSenseReader : IControllerReader
{
    private const int SonyVendorId = 0x054C;
    private static readonly int[] DualSenseProductIds = { 0x0CE6, 0x0DF2 };

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private HidStream? _stream;
    private HidDevice? _device;
    private readonly object _lock = new();

    public ConnectionType ConnectionType { get; private set; } = ConnectionType.Disconnected;
    public string? DeviceName { get; private set; }
    public ControllerKind Kind => ControllerKind.DualSense;
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
        _thread = new Thread(() => ReadLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "DualSenseReader",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _stream?.Close(); } catch { /* ignore */ }
        _thread?.Join(1000);
        _thread = null;
        _cts?.Dispose();
        _cts = null;
        SetConnection(ConnectionType.Disconnected);
    }

    public void Dispose() => Stop();

    private void ReadLoop(CancellationToken ct)
    {
        var buf = new byte[128];
        while (!ct.IsCancellationRequested)
        {
            if (_stream == null)
            {
                if (!TryConnect()) { Thread.Sleep(750); continue; }
            }

            try
            {
                var len = _stream!.Read(buf, 0, buf.Length);
                if (len > 0)
                {
                    var state = TryParse(buf, len);
                    if (state.HasValue)
                    {
                        LatestState = state.Value;
                        FrameReceived?.Invoke(state.Value);
                    }
                }
            }
            catch (TimeoutException)
            {
                // expected — lets us re-check the cancellation token
            }
            catch
            {
                Teardown();
                Thread.Sleep(500);
            }
        }
        Teardown();
    }

    private bool TryConnect()
    {
        try
        {
            var list = DeviceList.Local.GetHidDevices(SonyVendorId);
            HidDevice? candidate = null;
            foreach (var d in list)
            {
                if (Array.IndexOf(DualSenseProductIds, d.ProductID) >= 0)
                {
                    candidate = d;
                    break;
                }
            }
            if (candidate == null) return false;

            if (!candidate.TryOpen(out var stream)) return false;
            stream.ReadTimeout = 1000;

            lock (_lock)
            {
                _device = candidate;
                _stream = stream;
            }

            // Determine connection type by probing input report length.
            // USB: max input report ~64 bytes; BT: ~78 bytes for 0x31.
            var conn = candidate.GetMaxInputReportLength() > 64
                ? ConnectionType.Bluetooth
                : ConnectionType.Usb;

            // For BT, request feature report 0x05 (calibration data) — the side
            // effect is that the controller switches to extended report 0x31.
            if (conn == ConnectionType.Bluetooth)
            {
                try
                {
                    var feat = new byte[41];
                    feat[0] = 0x05;
                    stream.GetFeature(feat);
                }
                catch { /* harmless if it fails — many drivers still emit 0x31 */ }
            }

            DeviceName = ProductName(candidate.ProductID);
            SetConnection(conn);
            return true;
        }
        catch
        {
            Teardown();
            return false;
        }
    }

    private static string ProductName(int productId) => productId switch
    {
        0x0CE6 => "DualSense",
        0x0DF2 => "DualSense Edge",
        _      => "Controller",
    };

    private void Teardown()
    {
        lock (_lock)
        {
            try { _stream?.Close(); } catch { }
            _stream = null;
            _device = null;
        }
        DeviceName = null;
        SetConnection(ConnectionType.Disconnected);
    }

    private void SetConnection(ConnectionType c)
    {
        if (ConnectionType == c) return;
        ConnectionType = c;
        ConnectionChanged?.Invoke(c);
    }

    private static DualSenseState? TryParse(byte[] buf, int len)
    {
        if (len < 10) return null;
        var reportId = buf[0];
        int o; // offset to LX

        if (reportId == 0x01 && len >= 11)
        {
            // USB full report OR BT minimal report. Distinguish by length:
            // USB 0x01 is 64 bytes; BT minimal 0x01 is 10 bytes.
            // We accept both and pick offsets accordingly.
            if (len >= 64)
            {
                // USB full
                o = 1;
                return ParseStandard(buf, o, hasButtons3: true);
            }
            else
            {
                // BT minimal: layout differs — triggers come AFTER buttons.
                // [1]=LX [2]=LY [3]=RX [4]=RY [5]=Btn1 [6]=Btn2 [7]=Btn3 [8]=L2 [9]=R2
                return ParseBtMinimal(buf);
            }
        }
        if (reportId == 0x31 && len >= 14)
        {
            // BT extended: data shifts by +1 vs USB (extra tag byte at index 1).
            o = 2;
            return ParseStandard(buf, o, hasButtons3: true);
        }
        return null;
    }

    private static DualSenseState ParseStandard(byte[] buf, int o, bool hasButtons3)
    {
        float lx = NormStick(buf[o + 0]);
        float ly = -NormStick(buf[o + 1]); // invert: HID Y grows downward
        float rx = NormStick(buf[o + 2]);
        float ry = -NormStick(buf[o + 3]);
        float l2 = buf[o + 4] / 255f;
        float r2 = buf[o + 5] / 255f;
        // buf[o+6] = sequence counter
        byte b1 = buf[o + 7];
        byte b2 = buf[o + 8];
        byte b3 = hasButtons3 && (o + 9) < buf.Length ? buf[o + 9] : (byte)0;

        var btns = ParseButtons(b1, b2, b3);
        return new DualSenseState(lx, ly, rx, ry, l2, r2, btns, Stopwatch.GetTimestamp());
    }

    private static DualSenseState ParseBtMinimal(byte[] buf)
    {
        float lx = NormStick(buf[1]);
        float ly = -NormStick(buf[2]);
        float rx = NormStick(buf[3]);
        float ry = -NormStick(buf[4]);
        byte b1 = buf[5];
        byte b2 = buf[6];
        byte b3 = buf[7];
        float l2 = buf[8] / 255f;
        float r2 = buf[9] / 255f;
        var btns = ParseButtons(b1, b2, b3);
        return new DualSenseState(lx, ly, rx, ry, l2, r2, btns, Stopwatch.GetTimestamp());
    }

    private static float NormStick(byte raw)
    {
        // 0..255 with 128 nominal center → -1..1
        var v = (raw - 128) / 127f;
        return v < -1 ? -1 : v > 1 ? 1 : v;
    }

    private static DualSenseButton ParseButtons(byte b1, byte b2, byte b3)
    {
        DualSenseButton flags = DualSenseButton.None;

        // b1 low nibble: D-pad direction (0=N..7=NW, 8=None)
        int dpad = b1 & 0x0F;
        flags |= dpad switch
        {
            0 => DualSenseButton.DPadUp,
            1 => DualSenseButton.DPadUp | DualSenseButton.DPadRight,
            2 => DualSenseButton.DPadRight,
            3 => DualSenseButton.DPadDown | DualSenseButton.DPadRight,
            4 => DualSenseButton.DPadDown,
            5 => DualSenseButton.DPadDown | DualSenseButton.DPadLeft,
            6 => DualSenseButton.DPadLeft,
            7 => DualSenseButton.DPadUp | DualSenseButton.DPadLeft,
            _ => DualSenseButton.None,
        };
        // b1 high nibble: face buttons
        if ((b1 & 0x10) != 0) flags |= DualSenseButton.Square;
        if ((b1 & 0x20) != 0) flags |= DualSenseButton.Cross;
        if ((b1 & 0x40) != 0) flags |= DualSenseButton.Circle;
        if ((b1 & 0x80) != 0) flags |= DualSenseButton.Triangle;

        // b2: shoulder + start/select + sticks
        if ((b2 & 0x01) != 0) flags |= DualSenseButton.L1;
        if ((b2 & 0x02) != 0) flags |= DualSenseButton.R1;
        if ((b2 & 0x04) != 0) flags |= DualSenseButton.L2;
        if ((b2 & 0x08) != 0) flags |= DualSenseButton.R2;
        if ((b2 & 0x10) != 0) flags |= DualSenseButton.Share;
        if ((b2 & 0x20) != 0) flags |= DualSenseButton.Options;
        if ((b2 & 0x40) != 0) flags |= DualSenseButton.L3;
        if ((b2 & 0x80) != 0) flags |= DualSenseButton.R3;

        // b3: PS/Touchpad/Mute
        if ((b3 & 0x01) != 0) flags |= DualSenseButton.PS;
        if ((b3 & 0x02) != 0) flags |= DualSenseButton.Touchpad;
        if ((b3 & 0x04) != 0) flags |= DualSenseButton.Mute;

        return flags;
    }
}
