using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Aggregates a DualSense (HID) reader and an XInput (Xbox) reader into a
/// single <see cref="IControllerReader"/>. Both run concurrently — inputs
/// from any connected pad combine, and the connection display + mapping
/// labels in the UI reflect every connected controller type.
/// </summary>
public sealed class ControllerSource : IControllerReader
{
    private readonly DualSenseReader _dualSense = new();
    private readonly XInputReader _xinput = new();
    private readonly object _lock = new();

    public ConnectionType ConnectionType { get; private set; } = ConnectionType.Disconnected;
    public string? DeviceName { get; private set; }
    public ControllerKind Kind { get; private set; } = ControllerKind.None;
    public DualSenseState? LatestState
    {
        get
        {
            var ds = _dualSense.LatestState;
            var xb = _xinput.LatestState;
            if (ds is null) return xb;
            if (xb is null) return ds;
            return StateMerge.Combine(ds.Value, xb.Value);
        }
    }

    public event Action<ConnectionType>? ConnectionChanged;
    public event Action<DualSenseState>? FrameReceived;
    public event Action<ControllerKind>? KindChanged;

    public ControllerSource()
    {
        _dualSense.ConnectionChanged += _ => RefreshAggregates();
        _xinput.ConnectionChanged    += _ => RefreshAggregates();
        _dualSense.KindChanged       += _ => RefreshAggregates();
        _xinput.KindChanged          += _ => RefreshAggregates();
        _dualSense.FrameReceived     += s => FrameReceived?.Invoke(s);
        _xinput.FrameReceived        += s => FrameReceived?.Invoke(s);
    }

    public void Start()
    {
        _dualSense.Start();
        _xinput.Start();
    }

    public void Stop()
    {
        _dualSense.Stop();
        _xinput.Stop();
        RefreshAggregates();
    }

    public void Dispose()
    {
        _dualSense.Dispose();
        _xinput.Dispose();
    }

    private void RefreshAggregates()
    {
        ControllerKind newKind;
        ConnectionType newConn;
        string? newName;

        lock (_lock)
        {
            newKind = _dualSense.Kind | _xinput.Kind;

            // Aggregate connection: USB beats BT beats Disconnected.
            newConn = ConnectionType.Disconnected;
            foreach (var c in new[] { _dualSense.ConnectionType, _xinput.ConnectionType })
            {
                if (c == ConnectionType.Usb) { newConn = ConnectionType.Usb; break; }
                if (c == ConnectionType.Bluetooth) newConn = ConnectionType.Bluetooth;
            }

            // Compose device name: " + " between non-empty children.
            var dsName = _dualSense.DeviceName;
            var xbName = _xinput.DeviceName;
            newName = (dsName, xbName) switch
            {
                (null, null) => null,
                (var a,  null) => a,
                (null, var b)  => b,
                (var a, var b) => $"{a} + {b}",
            };
        }

        bool kindChanged, connChanged, nameChanged;
        lock (_lock)
        {
            kindChanged = Kind != newKind;
            connChanged = ConnectionType != newConn;
            nameChanged = DeviceName != newName;
            Kind = newKind;
            ConnectionType = newConn;
            DeviceName = newName;
        }

        if (connChanged || nameChanged) ConnectionChanged?.Invoke(newConn);
        if (kindChanged) KindChanged?.Invoke(newKind);
    }
}
