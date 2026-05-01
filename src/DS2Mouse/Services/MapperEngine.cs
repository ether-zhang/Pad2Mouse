using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Pulls latest <see cref="DualSenseState"/> from the reader on a fixed tick
/// and translates it into mouse/keyboard input via <see cref="InputSimulator"/>.
/// Hardcoded default mapping (M4); button-mapping customization deferred.
/// </summary>
public sealed class MapperEngine : IDisposable
{
    private const int TickIntervalMs = 8; // 125 Hz

    // Virtual-Key codes used by the default mapping.
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_LEFT  = 0x25;
    private const ushort VK_UP    = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN  = 0x28;

    private readonly DualSenseReader _reader;
    private readonly Timer _timer;
    private readonly object _tickLock = new();

    private DualSenseButton _prevButtons;
    private bool _prevR2, _prevL2;
    private float _scrollAccum;
    private long _lastTickStamp;
    private long _stickHoldStartMs; // 0 = stick is in deadzone

    public AppConfig Config { get; set; }

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            EnabledChanged?.Invoke(value);
        }
    }

    /// <summary>Returns true if input should be suppressed (e.g. fullscreen guard).</summary>
    public Func<bool> Gate { get; set; } = static () => false;

    public event Action<bool>? EnabledChanged;

    public MapperEngine(DualSenseReader reader, AppConfig config)
    {
        _reader = reader;
        Config = config;
        Enabled = config.Enabled;
        _timer = new Timer(_ => SafeTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TickIntervalMs, TickIntervalMs);
    public void Stop()  => _timer.Change(Timeout.Infinite, Timeout.Infinite);
    public void Dispose() => _timer.Dispose();

    private void SafeTick()
    {
        if (!Monitor.TryEnter(_tickLock)) return;
        try { Tick(); }
        finally { Monitor.Exit(_tickLock); }
    }

    private void Tick()
    {
        var snapshot = _reader.LatestState;
        if (snapshot is null) return;
        var s = snapshot.Value;

        // PS toggles Enabled regardless of gating, so user can re-enable from controller.
        var newlyPressed = s.Buttons & ~_prevButtons;
        if ((newlyPressed & DualSenseButton.PS) != 0)
        {
            Enabled = !Enabled;
        }

        var gated = !Enabled || Gate();
        if (gated)
        {
            // Release any held outputs from previous tick before idling.
            FlushHeldStates();
            _prevButtons = s.Buttons;
            _prevR2 = s.R2Trigger > Config.TriggerThreshold;
            _prevL2 = s.L2Trigger > Config.TriggerThreshold;
            _scrollAccum = 0;
            _stickHoldStartMs = 0;
            _lastTickStamp = Environment.TickCount64;
            return;
        }

        ProcessLeftStick(s);
        ProcessRightStick(s);
        ProcessTriggers(s);
        ProcessButtons(s, newlyPressed);

        _prevButtons = s.Buttons;
        _lastTickStamp = Environment.TickCount64;
    }

    private void ProcessLeftStick(DualSenseState s)
    {
        var (dx, dy) = ApplyStickCurve(
            s.LeftStickX, s.LeftStickY,
            Config.LeftStick.Deadzone,
            Config.LeftStick.Exponent,
            Config.LeftStick.Sensitivity);

        if (dx == 0f && dy == 0f)
        {
            _stickHoldStartMs = 0;
            return;
        }

        // Linear time-based acceleration: factor ramps from 1 to MaxFactor
        // over RampSeconds while the stick stays out of the deadzone.
        var now = Environment.TickCount64;
        if (_stickHoldStartMs == 0) _stickHoldStartMs = now;
        var heldMs = now - _stickHoldStartMs;
        var maxF = Config.LeftStick.AccelMaxFactor;
        var ramp = Config.LeftStick.AccelRampSeconds;
        float factor = (ramp <= 0f || maxF <= 1f)
            ? maxF
            : 1f + (maxF - 1f) * MathF.Min(1f, heldMs / (ramp * 1000f));
        dx *= factor;
        dy *= factor;

        // Y is positive-up in our normalized state, but screen Y grows downward.
        InputSimulator.MoveRelative((int)MathF.Round(dx), (int)MathF.Round(-dy));
    }

    private void ProcessRightStick(DualSenseState s)
    {
        var dz = Config.RightStick.Deadzone;
        var y = s.RightStickY;
        var mag = MathF.Abs(y);
        if (mag < dz) return;

        var rescaled = (mag - dz) / (1 - dz) * MathF.Sign(y);
        if (Config.RightStick.InvertVertical) rescaled = -rescaled;

        // notches/sec * 120 wheelDelta * dt(s)
        const float WheelDelta = 120f;
        float dt = TickIntervalMs / 1000f;
        _scrollAccum += rescaled * Config.RightStick.Speed * WheelDelta * dt;

        // Emit whole wheel-delta units; keep fractional remainder for smoothness.
        if (MathF.Abs(_scrollAccum) >= 1f)
        {
            int whole = (int)_scrollAccum;
            InputSimulator.ScrollVertical(whole);
            _scrollAccum -= whole;
        }
    }

    private void ProcessTriggers(DualSenseState s)
    {
        var thr = Config.TriggerThreshold;

        bool r2 = s.R2Trigger > thr;
        if (r2 != _prevR2)
        {
            if (r2) InputSimulator.MouseDown(MouseButton.Left);
            else    InputSimulator.MouseUp(MouseButton.Left);
            _prevR2 = r2;
        }

        bool l2 = s.L2Trigger > thr;
        if (l2 != _prevL2)
        {
            if (l2) InputSimulator.MouseDown(MouseButton.Right);
            else    InputSimulator.MouseUp(MouseButton.Right);
            _prevL2 = l2;
        }
    }

    private void ProcessButtons(DualSenseState s, DualSenseButton newlyPressed)
    {
        var released = _prevButtons & ~s.Buttons;

        // Click semantics — single fire on press.
        if ((newlyPressed & DualSenseButton.Cross)    != 0) InputSimulator.MouseClick(MouseButton.Left);
        if ((newlyPressed & DualSenseButton.Circle)   != 0) InputSimulator.MouseClick(MouseButton.Right);
        if ((newlyPressed & DualSenseButton.Square)   != 0) InputSimulator.MouseClick(MouseButton.Middle);
        if ((newlyPressed & DualSenseButton.Triangle) != 0) InputSimulator.KeyTap(VK_RETURN);

        // Hold semantics — D-Pad as arrow keys.
        HoldKey(newlyPressed, released, DualSenseButton.DPadUp,    VK_UP);
        HoldKey(newlyPressed, released, DualSenseButton.DPadDown,  VK_DOWN);
        HoldKey(newlyPressed, released, DualSenseButton.DPadLeft,  VK_LEFT);
        HoldKey(newlyPressed, released, DualSenseButton.DPadRight, VK_RIGHT);
    }

    private static void HoldKey(DualSenseButton newlyPressed, DualSenseButton released,
                                DualSenseButton trigger, ushort vk)
    {
        if ((newlyPressed & trigger) != 0) InputSimulator.KeyDown(vk);
        if ((released     & trigger) != 0) InputSimulator.KeyUp(vk);
    }

    private void FlushHeldStates()
    {
        // Release triggers
        if (_prevR2) InputSimulator.MouseUp(MouseButton.Left);
        if (_prevL2) InputSimulator.MouseUp(MouseButton.Right);
        _prevR2 = _prevL2 = false;

        // Release D-Pad-mapped arrows
        if ((_prevButtons & DualSenseButton.DPadUp)    != 0) InputSimulator.KeyUp(VK_UP);
        if ((_prevButtons & DualSenseButton.DPadDown)  != 0) InputSimulator.KeyUp(VK_DOWN);
        if ((_prevButtons & DualSenseButton.DPadLeft)  != 0) InputSimulator.KeyUp(VK_LEFT);
        if ((_prevButtons & DualSenseButton.DPadRight) != 0) InputSimulator.KeyUp(VK_RIGHT);
    }

    private static (float dx, float dy) ApplyStickCurve(
        float x, float y, float deadzone, float exponent, float sensitivity)
    {
        var mag = MathF.Sqrt(x * x + y * y);
        if (mag < deadzone) return (0f, 0f);

        var rescaled = (mag - deadzone) / (1f - deadzone);
        if (rescaled > 1f) rescaled = 1f;

        var curved = MathF.Pow(rescaled, exponent);
        var scale = curved / mag * sensitivity;
        return (x * scale, y * scale);
    }
}
