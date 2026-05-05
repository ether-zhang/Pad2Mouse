using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Common interface for any controller source that produces
/// <see cref="DualSenseState"/> frames. The state struct is reused as a
/// neutral shape — XInput devices populate the same fields (e.g. A → Cross).
/// </summary>
public interface IControllerReader : IDisposable
{
    ConnectionType ConnectionType { get; }
    string? DeviceName { get; }
    ControllerKind Kind { get; }
    DualSenseState? LatestState { get; }

    /// <summary>One entry per attached physical controller, in display order.
    /// Used by the UI to render multi-device status as separate lines with
    /// per-device connection type. Empty when nothing is connected.</summary>
    IReadOnlyList<ConnectedDevice> ConnectedDevices { get; }

    event Action<ConnectionType>? ConnectionChanged;
    event Action<DualSenseState>? FrameReceived;

    /// <summary>Fires when the active controller type changes (e.g. user
    /// unplugs DualSense and Xbox takes over). Implementations that only
    /// produce one kind never fire this.</summary>
    event Action<ControllerKind>? KindChanged;

    void Start();
    void Stop();
}

public readonly record struct ConnectedDevice(string Name, ConnectionType ConnectionType);
