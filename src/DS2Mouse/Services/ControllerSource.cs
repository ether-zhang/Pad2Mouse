using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Multiplexes a DualSense (HID) reader and an XInput (Xbox) reader into a
/// single <see cref="IControllerReader"/>. Whichever reader connects first
/// becomes active; if it later disconnects and the other is connected, the
/// active role swaps automatically. <see cref="KindChanged"/> fires on swap
/// so consumers can refresh controller-specific UI labels.
/// </summary>
public sealed class ControllerSource : IControllerReader
{
    private readonly DualSenseReader _dualSense = new();
    private readonly XInputReader _xinput = new();
    private readonly object _lock = new();

    private IControllerReader? _active;

    public ConnectionType ConnectionType => _active?.ConnectionType ?? ConnectionType.Disconnected;
    public string? DeviceName => _active?.DeviceName;
    public ControllerKind Kind => _active?.Kind ?? ControllerKind.None;
    public DualSenseState? LatestState => _active?.LatestState;

    public event Action<ConnectionType>? ConnectionChanged;
    public event Action<DualSenseState>? FrameReceived;
    public event Action<ControllerKind>? KindChanged;

    public ControllerSource()
    {
        _dualSense.ConnectionChanged += c => OnChildConnection(_dualSense, c);
        _xinput.ConnectionChanged    += c => OnChildConnection(_xinput, c);
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
        SwitchActive(null);
    }

    public void Dispose()
    {
        _dualSense.Dispose();
        _xinput.Dispose();
    }

    private void OnChildConnection(IControllerReader who, ConnectionType c)
    {
        lock (_lock)
        {
            if (c != ConnectionType.Disconnected)
            {
                // First-connected wins. If we already have an active reader,
                // ignore the new one until the active one drops.
                if (_active == null) SwitchActive(who);
                else if (ReferenceEquals(_active, who))
                {
                    // Same reader's connection refreshed (e.g. USB → BT for
                    // DualSense) — re-emit so listeners pick up the new label.
                    ConnectionChanged?.Invoke(c);
                }
                return;
            }

            // Disconnect.
            if (!ReferenceEquals(_active, who)) return;

            // The active reader dropped — promote the other one if it's up.
            var fallback = ReferenceEquals(who, _dualSense) ? (IControllerReader)_xinput : _dualSense;
            if (fallback.ConnectionType != ConnectionType.Disconnected)
                SwitchActive(fallback);
            else
                SwitchActive(null);
        }
    }

    private void SwitchActive(IControllerReader? next)
    {
        var prev = _active;
        if (ReferenceEquals(prev, next)) return;

        if (prev != null) prev.FrameReceived -= ForwardFrame;
        _active = next;
        if (next != null) next.FrameReceived += ForwardFrame;

        var newKind = next?.Kind ?? ControllerKind.None;
        var newConn = next?.ConnectionType ?? ConnectionType.Disconnected;
        KindChanged?.Invoke(newKind);
        ConnectionChanged?.Invoke(newConn);
    }

    private void ForwardFrame(DualSenseState s) => FrameReceived?.Invoke(s);
}
