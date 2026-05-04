using System.Diagnostics;
using DS2Mouse.Models;
using HidSharp;

namespace DS2Mouse.Services;

/// <summary>
/// Opens every matching DualSense / DualSense Edge HID device and merges
/// their inputs into a single <see cref="DualSenseState"/>. A supervisor
/// thread re-scans periodically to pick up newly attached pads; each open
/// device runs its own blocking-read thread.
/// </summary>
public sealed class DualSenseReader : IControllerReader
{
    private const int SonyVendorId = 0x054C;
    private static readonly int[] DualSenseProductIds = { 0x0CE6, 0x0DF2 };
    private const int ScanIntervalMs = 750;

    private CancellationTokenSource? _cts;
    private Thread? _supervisor;
    private readonly object _slotsLock = new();
    private readonly List<DeviceSlot> _slots = new();

    public ConnectionType ConnectionType { get; private set; } = ConnectionType.Disconnected;
    public string? DeviceName { get; private set; }
    public ControllerKind Kind { get; private set; } = ControllerKind.None;
    public DualSenseState? LatestState { get; private set; }

    public event Action<ConnectionType>? ConnectionChanged;
    public event Action<DualSenseState>? FrameReceived;
    public event Action<ControllerKind>? KindChanged;

    public void Start()
    {
        if (_supervisor != null) return;
        _cts = new CancellationTokenSource();
        _supervisor = new Thread(() => SupervisorLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "DualSense-Supervisor",
        };
        _supervisor.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        ShutdownAllSlots();
        _supervisor?.Join(1500);
        _supervisor = null;
        _cts?.Dispose();
        _cts = null;
        UpdateAggregates();
    }

    public void Dispose() => Stop();

    private void SupervisorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScanAndAttach(ct);
            PruneDeadSlots();
            UpdateAggregates();
            try { ct.WaitHandle.WaitOne(ScanIntervalMs); } catch { }
        }
    }

    private void ScanAndAttach(CancellationToken ct)
    {
        try
        {
            var list = DeviceList.Local.GetHidDevices(SonyVendorId);
            foreach (var d in list)
            {
                if (Array.IndexOf(DualSenseProductIds, d.ProductID) < 0) continue;
                bool already;
                lock (_slotsLock) already = _slots.Any(s => s.DevicePath == d.DevicePath);
                if (already) continue;
                TryAttach(d, ct);
            }
        }
        catch
        {
            // Enumeration failures are transient — try again next cycle.
        }
    }

    private void TryAttach(HidDevice device, CancellationToken parentCt)
    {
        HidStream? stream = null;
        try
        {
            if (!device.TryOpen(out stream)) return;
            stream.ReadTimeout = 1000;

            // USB max input report ~64 bytes; BT ~78 for 0x31.
            var conn = device.GetMaxInputReportLength() > 64
                ? ConnectionType.Bluetooth
                : ConnectionType.Usb;

            // For BT, request feature report 0x05 to switch to extended report 0x31.
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

            var slot = new DeviceSlot
            {
                DevicePath = device.DevicePath,
                Stream = stream,
                ConnectionType = conn,
                ProductName = ProductName(device.ProductID),
                Cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt),
            };
            slot.Thread = new Thread(() => ReadSlot(slot))
            {
                IsBackground = true,
                Name = $"DualSense-Read-{slot.ProductName}",
            };
            lock (_slotsLock) _slots.Add(slot);
            slot.Thread.Start();
            stream = null; // ownership transferred to slot
        }
        catch
        {
            try { stream?.Close(); } catch { }
        }
    }

    private void ReadSlot(DeviceSlot slot)
    {
        var buf = new byte[128];
        var ct = slot.Cts!.Token;
        while (!ct.IsCancellationRequested && !slot.Dead)
        {
            try
            {
                var len = slot.Stream.Read(buf, 0, buf.Length);
                if (len > 0)
                {
                    var state = TryParse(buf, len);
                    if (state.HasValue)
                    {
                        slot.LatestState = state.Value;
                        FrameReceived?.Invoke(state.Value);
                        RecomputeMergedLatest();
                    }
                }
            }
            catch (TimeoutException)
            {
                // expected — re-check cancellation and loop
            }
            catch
            {
                slot.Dead = true;
                break;
            }
        }
        try { slot.Stream.Close(); } catch { }
    }

    private void RecomputeMergedLatest()
    {
        DeviceSlot[] copy;
        lock (_slotsLock) copy = _slots.ToArray();
        DualSenseState? merged = null;
        foreach (var s in copy)
        {
            if (s.LatestState is not { } st) continue;
            merged = merged is null ? st : StateMerge.Combine(merged.Value, st);
        }
        LatestState = merged;
    }

    private void PruneDeadSlots()
    {
        List<DeviceSlot> dead = new();
        lock (_slotsLock)
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                if (_slots[i].Dead)
                {
                    dead.Add(_slots[i]);
                    _slots.RemoveAt(i);
                }
            }
        }
        foreach (var s in dead)
        {
            try { s.Cts?.Cancel(); } catch { }
            try { s.Stream.Close(); } catch { }
            s.Thread?.Join(300);
            s.Cts?.Dispose();
        }
    }

    private void ShutdownAllSlots()
    {
        DeviceSlot[] copy;
        lock (_slotsLock) { copy = _slots.ToArray(); _slots.Clear(); }
        foreach (var s in copy)
        {
            try { s.Cts?.Cancel(); } catch { }
            try { s.Stream.Close(); } catch { }
            s.Thread?.Join(500);
            s.Cts?.Dispose();
        }
    }

    private void UpdateAggregates()
    {
        DeviceSlot[] copy;
        lock (_slotsLock) copy = _slots.ToArray();

        // Aggregate ConnectionType: USB beats BT; none → Disconnected.
        var newConn = copy.Length == 0
            ? ConnectionType.Disconnected
            : copy.Any(s => s.ConnectionType == ConnectionType.Usb)
                ? ConnectionType.Usb
                : ConnectionType.Bluetooth;
        var newKind = copy.Length > 0 ? ControllerKind.DualSense : ControllerKind.None;
        var newName = BuildDeviceName(copy);

        bool nameChanged = DeviceName != newName;
        bool connChanged = ConnectionType != newConn;
        bool kindChanged = Kind != newKind;

        DeviceName = newName;
        ConnectionType = newConn;
        Kind = newKind;
        if (copy.Length == 0) LatestState = null;

        if (connChanged || nameChanged) ConnectionChanged?.Invoke(newConn);
        if (kindChanged) KindChanged?.Invoke(newKind);
    }

    private static string? BuildDeviceName(DeviceSlot[] slots)
    {
        if (slots.Length == 0) return null;

        // Group by product name; only suffix when a group has more than one.
        var groupCounts = new Dictionary<string, int>();
        foreach (var s in slots)
            groupCounts[s.ProductName] = groupCounts.GetValueOrDefault(s.ProductName) + 1;

        var perGroupIndex = new Dictionary<string, int>();
        var parts = new List<string>(slots.Length);
        foreach (var s in slots)
        {
            if (groupCounts[s.ProductName] == 1)
            {
                parts.Add(s.ProductName);
            }
            else
            {
                int n = perGroupIndex.GetValueOrDefault(s.ProductName) + 1;
                perGroupIndex[s.ProductName] = n;
                parts.Add($"{s.ProductName}-{n}");
            }
        }
        return string.Join(" + ", parts);
    }

    private static string ProductName(int productId) => productId switch
    {
        0x0CE6 => "DualSense",
        0x0DF2 => "DualSense Edge",
        _      => "Controller",
    };

    private static DualSenseState? TryParse(byte[] buf, int len)
    {
        if (len < 10) return null;
        var reportId = buf[0];
        int o;

        if (reportId == 0x01 && len >= 11)
        {
            if (len >= 64)
            {
                o = 1;
                return ParseStandard(buf, o, hasButtons3: true);
            }
            return ParseBtMinimal(buf);
        }
        if (reportId == 0x31 && len >= 14)
        {
            o = 2;
            return ParseStandard(buf, o, hasButtons3: true);
        }
        return null;
    }

    private static DualSenseState ParseStandard(byte[] buf, int o, bool hasButtons3)
    {
        float lx = NormStick(buf[o + 0]);
        float ly = -NormStick(buf[o + 1]);
        float rx = NormStick(buf[o + 2]);
        float ry = -NormStick(buf[o + 3]);
        float l2 = buf[o + 4] / 255f;
        float r2 = buf[o + 5] / 255f;
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
        var v = (raw - 128) / 127f;
        return v < -1 ? -1 : v > 1 ? 1 : v;
    }

    private static DualSenseButton ParseButtons(byte b1, byte b2, byte b3)
    {
        DualSenseButton flags = DualSenseButton.None;

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
        if ((b1 & 0x10) != 0) flags |= DualSenseButton.Square;
        if ((b1 & 0x20) != 0) flags |= DualSenseButton.Cross;
        if ((b1 & 0x40) != 0) flags |= DualSenseButton.Circle;
        if ((b1 & 0x80) != 0) flags |= DualSenseButton.Triangle;

        if ((b2 & 0x01) != 0) flags |= DualSenseButton.L1;
        if ((b2 & 0x02) != 0) flags |= DualSenseButton.R1;
        if ((b2 & 0x04) != 0) flags |= DualSenseButton.L2;
        if ((b2 & 0x08) != 0) flags |= DualSenseButton.R2;
        if ((b2 & 0x10) != 0) flags |= DualSenseButton.Share;
        if ((b2 & 0x20) != 0) flags |= DualSenseButton.Options;
        if ((b2 & 0x40) != 0) flags |= DualSenseButton.L3;
        if ((b2 & 0x80) != 0) flags |= DualSenseButton.R3;

        if ((b3 & 0x01) != 0) flags |= DualSenseButton.PS;
        if ((b3 & 0x02) != 0) flags |= DualSenseButton.Touchpad;
        if ((b3 & 0x04) != 0) flags |= DualSenseButton.Mute;

        return flags;
    }

    private sealed class DeviceSlot
    {
        public required string DevicePath { get; init; }
        public required HidStream Stream { get; init; }
        public required ConnectionType ConnectionType { get; init; }
        public required string ProductName { get; init; }
        public CancellationTokenSource? Cts;
        public Thread? Thread;
        public DualSenseState? LatestState;
        public volatile bool Dead;
    }
}
