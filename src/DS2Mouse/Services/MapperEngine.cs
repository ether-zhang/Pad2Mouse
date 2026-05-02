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
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SPACE  = 0x20;
    private const ushort VK_LEFT   = 0x25;
    private const ushort VK_UP     = 0x26;
    private const ushort VK_RIGHT  = 0x27;
    private const ushort VK_DOWN   = 0x28;

    private readonly DualSenseReader _reader;
    private readonly Timer _timer;
    private readonly object _tickLock = new();

    private DualSenseButton _prevButtons;
    private bool _r2crossPrev;       // aggregated R2 trigger OR Cross button
    private bool _l2Prev;
    private bool _circlePrev, _squarePrev, _trianglePrev;
    private float _scrollAccum;
    private long _lastTickStamp;
    private long _stickHoldStartMs;  // 0 = left stick is in deadzone
    private long _scrollHoldStartMs; // 0 = right stick is in deadzone

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

        // L3+R3 (both sticks clicked together) toggles Enabled regardless of
        // gating, so the user can re-enable from the controller. The PS button
        // is avoided because Steam / Game Bar / system shells intercept it.
        const DualSenseButton ToggleCombo = DualSenseButton.L3 | DualSenseButton.R3;
        var newlyPressed = s.Buttons & ~_prevButtons;
        bool comboNow  = (s.Buttons    & ToggleCombo) == ToggleCombo;
        bool comboPrev = (_prevButtons & ToggleCombo) == ToggleCombo;
        if (comboNow && !comboPrev)
        {
            Enabled = !Enabled;
        }

        var gated = !Enabled || Gate();
        if (gated)
        {
            // Release any held outputs from previous tick before idling.
            FlushHeldStates();
            _prevButtons = s.Buttons;
            _scrollAccum = 0;
            _stickHoldStartMs = 0;
            _scrollHoldStartMs = 0;
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
        if (mag < dz)
        {
            _scrollHoldStartMs = 0;
            return;
        }

        var rescaled = (mag - dz) / (1 - dz) * MathF.Sign(y);
        if (Config.RightStick.InvertVertical) rescaled = -rescaled;

        // Linear time-based acceleration mirroring the left stick.
        var now = Environment.TickCount64;
        if (_scrollHoldStartMs == 0) _scrollHoldStartMs = now;
        var heldMs = now - _scrollHoldStartMs;
        var maxF = Config.RightStick.AccelMaxFactor;
        var ramp = Config.RightStick.AccelRampSeconds;
        float factor = (ramp <= 0f || maxF <= 1f)
            ? maxF
            : 1f + (maxF - 1f) * MathF.Min(1f, heldMs / (ramp * 1000f));

        // notches/sec * 120 wheelDelta * dt(s)
        const float WheelDelta = 120f;
        float dt = TickIntervalMs / 1000f;
        _scrollAccum += rescaled * Config.RightStick.Speed * factor * WheelDelta * dt;

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
        var m = Config.Mappings;

        // R2 trigger and Cross share one configurable slot, OR-aggregated so
        // pressing one over the other doesn't release the action prematurely.
        bool r2c = s.R2Trigger > thr || (s.Buttons & DualSenseButton.Cross) != 0;
        DispatchInputEdge(m.R2OrCross, r2c, ref _r2crossPrev);

        bool l2 = s.L2Trigger > thr;
        DispatchInputEdge(m.L2, l2, ref _l2Prev);
    }

    private void ProcessButtons(DualSenseState s, DualSenseButton newlyPressed)
    {
        var released = _prevButtons & ~s.Buttons;
        var m = Config.Mappings;

        bool circle   = (s.Buttons & DualSenseButton.Circle)   != 0;
        bool square   = (s.Buttons & DualSenseButton.Square)   != 0;
        bool triangle = (s.Buttons & DualSenseButton.Triangle) != 0;
        DispatchInputEdge(m.Circle,   circle,   ref _circlePrev);
        DispatchInputEdge(m.Square,   square,   ref _squarePrev);
        DispatchInputEdge(m.Triangle, triangle, ref _trianglePrev);

        // D-Pad → arrow keys (not user-configurable for now).
        HoldKey(newlyPressed, released, DualSenseButton.DPadUp,    VK_UP);
        HoldKey(newlyPressed, released, DualSenseButton.DPadDown,  VK_DOWN);
        HoldKey(newlyPressed, released, DualSenseButton.DPadLeft,  VK_LEFT);
        HoldKey(newlyPressed, released, DualSenseButton.DPadRight, VK_RIGHT);
    }

    /// <summary>Apply a configurable action on the rising/falling edge of an
    /// input. Hold-type actions get matched down/up; click/tap actions fire
    /// only on the rising edge.</summary>
    private static void DispatchInputEdge(string action, bool isPressed, ref bool wasPressed)
    {
        if (ButtonActions.IsHold(action))
        {
            if (isPressed && !wasPressed) ApplyDown(action);
            else if (!isPressed && wasPressed) ApplyUp(action);
        }
        else
        {
            if (isPressed && !wasPressed) ApplyDown(action);
        }
        wasPressed = isPressed;
    }

    private static void ApplyDown(string action)
    {
        switch (action)
        {
            case ButtonActions.LeftClick:   InputSimulator.MouseClick(MouseButton.Left);   break;
            case ButtonActions.RightClick:  InputSimulator.MouseClick(MouseButton.Right);  break;
            case ButtonActions.MiddleClick: InputSimulator.MouseClick(MouseButton.Middle); break;
            case ButtonActions.LeftHold:    InputSimulator.MouseDown(MouseButton.Left);    break;
            case ButtonActions.RightHold:   InputSimulator.MouseDown(MouseButton.Right);   break;
            case ButtonActions.MiddleHold:  InputSimulator.MouseDown(MouseButton.Middle);  break;
            case ButtonActions.Enter:       InputSimulator.KeyTap(VK_RETURN);              break;
            case ButtonActions.Escape:      InputSimulator.KeyTap(VK_ESCAPE);              break;
            case ButtonActions.Space:       InputSimulator.KeyTap(VK_SPACE);               break;
            // None / unknown: no-op
        }
    }

    private static void ApplyUp(string action)
    {
        switch (action)
        {
            case ButtonActions.LeftHold:   InputSimulator.MouseUp(MouseButton.Left);   break;
            case ButtonActions.RightHold:  InputSimulator.MouseUp(MouseButton.Right);  break;
            case ButtonActions.MiddleHold: InputSimulator.MouseUp(MouseButton.Middle); break;
            // Click / tap actions don't track release.
        }
    }

    private static void HoldKey(DualSenseButton newlyPressed, DualSenseButton released,
                                DualSenseButton trigger, ushort vk)
    {
        if ((newlyPressed & trigger) != 0) InputSimulator.KeyDown(vk);
        if ((released     & trigger) != 0) InputSimulator.KeyUp(vk);
    }

    private void FlushHeldStates()
    {
        var m = Config.Mappings;
        if (_r2crossPrev)  ApplyUp(m.R2OrCross);
        if (_l2Prev)       ApplyUp(m.L2);
        if (_circlePrev)   ApplyUp(m.Circle);
        if (_squarePrev)   ApplyUp(m.Square);
        if (_trianglePrev) ApplyUp(m.Triangle);
        _r2crossPrev = _l2Prev = _circlePrev = _squarePrev = _trianglePrev = false;

        // Release D-Pad-mapped arrows
        if ((_prevButtons & DualSenseButton.DPadUp)    != 0) InputSimulator.KeyUp(VK_UP);
        if ((_prevButtons & DualSenseButton.DPadDown)  != 0) InputSimulator.KeyUp(VK_DOWN);
        if ((_prevButtons & DualSenseButton.DPadLeft)  != 0) InputSimulator.KeyUp(VK_LEFT);
        if ((_prevButtons & DualSenseButton.DPadRight) != 0) InputSimulator.KeyUp(VK_RIGHT);
    }

    /// <summary>Releases any outputs currently held under the existing mapping.
    /// Call this from the GUI BEFORE swapping a mapping entry so the old
    /// hold action is properly closed; the next tick starts the new action
    /// from a clean state.</summary>
    public void ReleaseHeldInputs()
    {
        lock (_tickLock)
        {
            FlushHeldStates();
        }
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
