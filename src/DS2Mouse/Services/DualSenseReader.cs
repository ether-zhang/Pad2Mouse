using System.Diagnostics;
using System.Linq;
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
    public IReadOnlyList<ConnectedDevice> ConnectedDevices { get; private set; } = Array.Empty<ConnectedDevice>();

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

                // Already attached on this exact HID node — skip.
                bool already;
                lock (_slotsLock) already = _slots.Any(s => s.DevicePath == d.DevicePath);
                if (already) continue;

                // Same physical controller via the OTHER transport (BT vs USB)?
                // The pad's serial number is stable across connection types, so
                // we use it to detect the duplicate. Without a serial we can't
                // dedupe — fall through to attach (worst case: two slots until
                // one drops, which is the existing behavior anyway).
                var newConn = d.GetMaxInputReportLength() > 64
                    ? ConnectionType.Bluetooth
                    : ConnectionType.Usb;
                var serial = SafeGetSerial(d);
                DeviceSlot? supersedes = null;
                if (serial != null)
                {
                    DeviceSlot? sameSerial;
                    lock (_slotsLock)
                        sameSerial = _slots.FirstOrDefault(
                            s => !s.Dead && s.SerialNumber == serial);

                    if (sameSerial != null)
                    {
                        // Prefer USB over BT — same controller, USB is lower latency
                        // and survives sleep better. If the new connection isn't an
                        // upgrade we just skip; the existing slot keeps streaming.
                        bool upgrade = sameSerial.ConnectionType == ConnectionType.Bluetooth
                                       && newConn == ConnectionType.Usb;
                        if (!upgrade) continue;
                        supersedes = sameSerial;
                    }
                }

                TryAttach(d, ct, serial, supersedes);
            }
        }
        catch
        {
            // Enumeration failures are transient — try again next cycle.
        }
    }

    private static string? SafeGetSerial(HidDevice d)
    {
        try { return d.GetSerialNumber(); }
        catch { return null; }
    }

    private void TryAttach(HidDevice device, CancellationToken parentCt,
                           string? serial, DeviceSlot? supersedes)
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
                SerialNumber = serial,
                Cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt),
            };
            slot.Thread = new Thread(() => ReadSlot(slot))
            {
                IsBackground = true,
                Name = $"DualSense-Read-{slot.ProductName}",
            };
            lock (_slotsLock)
            {
                // Mark the old transport's slot dead only after the new one is
                // ready — if open had failed we'd want to keep the old slot
                // streaming. PruneDeadSlots picks it up on the next cycle.
                if (supersedes != null) supersedes.Dead = true;
                _slots.Add(slot);
            }
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
        var newDevices = BuildConnectedDevices(copy);
        var newName = newDevices.Count == 0 ? null : string.Join(" + ", newDevices.Select(d => d.Name));

        bool nameChanged = DeviceName != newName;
        bool connChanged = ConnectionType != newConn;
        bool kindChanged = Kind != newKind;

        DeviceName = newName;
        ConnectionType = newConn;
        Kind = newKind;
        ConnectedDevices = newDevices;
        if (copy.Length == 0) LatestState = null;

        if (connChanged || nameChanged) ConnectionChanged?.Invoke(newConn);
        if (kindChanged) KindChanged?.Invoke(newKind);
    }

    // Per-slot {Name, ConnectionType} entries. Names follow the same suffix
    // rule as the aggregate string: bare product name when only one of that
    // product is attached; "ProductName-N" when multiple of the same product.
    private static IReadOnlyList<ConnectedDevice> BuildConnectedDevices(DeviceSlot[] slots)
    {
        if (slots.Length == 0) return Array.Empty<ConnectedDevice>();

        var groupCounts = new Dictionary<string, int>();
        foreach (var s in slots)
            groupCounts[s.ProductName] = groupCounts.GetValueOrDefault(s.ProductName) + 1;

        var perGroupIndex = new Dictionary<string, int>();
        var list = new List<ConnectedDevice>(slots.Length);
        foreach (var s in slots)
        {
            string name;
            if (groupCounts[s.ProductName] == 1)
            {
                name = s.ProductName;
            }
            else
            {
                int n = perGroupIndex.GetValueOrDefault(s.ProductName) + 1;
                perGroupIndex[s.ProductName] = n;
                name = $"{s.ProductName}-{n}";
            }
            list.Add(new ConnectedDevice(name, s.ConnectionType));
        }
        return list;
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

        if (reportId == 0x01 && len >= 10)
        {
            if (len >= 64)
            {
                o = 1;
                return ParseStandard(buf, len, o);
            }
            return ParseBtMinimal(buf);
        }
        if (reportId == 0x31 && len >= 14)
        {
            o = 2;
            return ParseStandard(buf, len, o);
        }
        return null;
    }

    private static DualSenseState ParseStandard(byte[] buf, int len, int o)
    {
        float lx = NormStick(buf[o + 0]);
        float ly = -NormStick(buf[o + 1]);
        float rx = NormStick(buf[o + 2]);
        float ry = -NormStick(buf[o + 3]);
        float l2 = buf[o + 4] / 255f;
        float r2 = buf[o + 5] / 255f;
        byte b1 = buf[o + 7];
        byte b2 = buf[o + 8];
        byte b3 = (o + 9) < len ? buf[o + 9] : (byte)0;
        bool hasTouchpadData = (o + 39) < len;
        var touch1 = hasTouchpadData ? ParseTouchContact(buf, o + 32) : default;
        var touch2 = hasTouchpadData ? ParseTouchContact(buf, o + 36) : default;

        var btns = ParseButtons(b1, b2, b3);
        return new DualSenseState(
            lx, ly, rx, ry, l2, r2, btns,
            hasTouchpadData, touch1, touch2,
            Stopwatch.GetTimestamp());
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
        return new DualSenseState(
            lx, ly, rx, ry, l2, r2, btns,
            HasTouchpadData: false, Touch1: default, Touch2: default,
            Stopwatch.GetTimestamp());
    }

    private static TouchContact ParseTouchContact(byte[] buf, int offset)
    {
        byte contact = buf[offset];
        bool active = (contact & 0x80) == 0;
        byte id = (byte)(contact & 0x7F);
        if (!active) return new TouchContact(false, id, 0, 0);

        ushort x = (ushort)(buf[offset + 1] | ((buf[offset + 2] & 0x0F) << 8));
        ushort y = (ushort)((buf[offset + 2] >> 4) | (buf[offset + 3] << 4));
        return new TouchContact(true, id, x, y);
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
        // Serial number identifies the physical controller across connection
        // types (USB / BT). Used to dedupe a single controller that appears
        // simultaneously on both transports. Null means the device didn't
        // expose one — fall back to DevicePath uniqueness.
        public string? SerialNumber { get; init; }
        public CancellationTokenSource? Cts;
        public Thread? Thread;
        public DualSenseState? LatestState;
        public volatile bool Dead;
    }
}
