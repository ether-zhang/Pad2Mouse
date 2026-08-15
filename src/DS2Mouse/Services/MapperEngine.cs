using System.Diagnostics;
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
    private const float TouchpadSensitivityDivisor = 60f;
    private const int TouchpadMaxDeltaPerTick = 400;
    private const int TouchpadWidth = 1920;

    // Virtual-Key codes used by the default mapping.
    private const ushort VK_BACK    = 0x08;
    private const ushort VK_TAB     = 0x09;
    private const ushort VK_RETURN  = 0x0D;
    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU    = 0x12; // Alt
    private const ushort VK_ESCAPE  = 0x1B;
    private const ushort VK_SPACE   = 0x20;
    private const ushort VK_LEFT    = 0x25;
    private const ushort VK_UP      = 0x26;
    private const ushort VK_RIGHT   = 0x27;
    private const ushort VK_DOWN    = 0x28;

    private readonly IControllerReader _reader;
    private readonly Timer _timer;
    private readonly object _tickLock = new();

    private DualSenseButton _prevButtons;
    private bool _r2Prev, _crossPrev;
    private bool _l2Prev;
    private bool _circlePrev, _squarePrev, _trianglePrev;
    private bool _l3Prev, _r3Prev;
    private bool _touchLeftPrev, _touchCenterPrev, _touchRightPrev;
    private bool _l3r3ComboActive;
    private TouchpadRegion _touchpadClickRegion;
    private bool _touchMoveActive;
    private byte _touchMoveId;
    private int _touchMoveSlot;
    private ushort _touchMoveX, _touchMoveY;
    private float _touchMoveAccumX, _touchMoveAccumY;
    private float _gyroAccumX, _gyroAccumY;
    private long _lastGyroFrameTicks;
    private uint _lastGyroSensorTimestamp;
    private float _gravityX, _gravityY, _gravityZ;
    private bool _hasGravity;
    private int _lastTouchX = TouchpadWidth / 2;
    private long _lastTouchSeenMs;
    private float _scrollAccum;
    private long _lastTickStamp;
    private long _stickHoldStartMs;  // 0 = left stick is in deadzone
    private long _scrollHoldStartMs; // 0 = right stick is in deadzone

    private enum TouchpadRegion
    {
        None,
        Left,
        Center,
        Right,
    }

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

    /// <summary>Invoked on the rising edge of the on-screen-keyboard combo (L1+R1).
    /// The caller wires this to show/hide our custom keyboard window.</summary>
    public Action OnSystemKeyboardToggle { get; set; } = static () => { };

    public event Action<bool>? EnabledChanged;

    public MapperEngine(IControllerReader reader, AppConfig config)
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
        if (snapshot is null)
        {
            FlushHeldStates();
            _prevButtons = DualSenseButton.None;
            return;
        }
        var s = snapshot.Value;

        // Tell the shell-nav suppressor the pad is in use so it only drops
        // Win11's injected nav keys while we're actually driving it — not the
        // keys a remote-desktop / streaming host injects when the pad is idle.
        if (IsControllerActive(s)) ShellInputSuppressor.NotifyGamepadActivity();

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
            ResetTouchpadMotion();
            _lastTickStamp = Environment.TickCount64;
            return;
        }

        ProcessLeftStick(s);
        ProcessGyro(s);
        ProcessRightStick(s);
        ProcessTouchpad(s);
        ProcessTriggers(s);
        ProcessButtons(s, newlyPressed);
        ProcessSystemShortcuts(s);

        _prevButtons = s.Buttons;
        _lastTickStamp = Environment.TickCount64;
    }

    // Genuine controller activity: any button down, or a stick pushed well
    // past drift. The threshold sits above resting noise but below Win11's
    // stick-as-nav trigger, so the suppression window is already open by the
    // time Win11 injects an arrow from stick deflection.
    private static bool IsControllerActive(in DualSenseState s)
    {
        const float Thr = 0.3f;
        return s.Buttons != DualSenseButton.None
            || MathF.Abs(s.LeftStickX)  > Thr || MathF.Abs(s.LeftStickY)  > Thr
            || MathF.Abs(s.RightStickX) > Thr || MathF.Abs(s.RightStickY) > Thr
            || (s.HasGyroData && (MathF.Abs(s.GyroX) > 3f
                || MathF.Abs(s.GyroY) > 3f || MathF.Abs(s.GyroZ) > 3f))
            || s.Touch1.Active || s.Touch2.Active;
    }

    private void ProcessGyro(in DualSenseState s)
    {
        var config = Config.Gyro;
        if (!config.Enabled || !s.HasGyroData || !IsGyroActive(s, config.Activation))
        {
            ResetGyroMotion();
            return;
        }

        // The mapper ticks independently of HID reports. Consume each sensor
        // frame once so a slow transport cannot repeat the same rotation.
        long frameTicks = s.GyroTimestampTicks;
        if (frameTicks == _lastGyroFrameTicks) return;
        if (_lastGyroFrameTicks == 0)
        {
            _lastGyroFrameTicks = frameTicks;
            _lastGyroSensorTimestamp = s.SensorTimestamp;
            UpdateGravity(s, 0f);
            return;
        }

        float hostDt = (frameTicks - _lastGyroFrameTicks) / (float)Stopwatch.Frequency;
        uint sensorDelta = unchecked(s.SensorTimestamp - _lastGyroSensorTimestamp);
        float sensorDt = sensorDelta / 3_000_000f; // DualSense timestamp units are about 0.33 us.
        float dt = sensorDt is > 0f and <= 0.05f ? sensorDt : hostDt;
        _lastGyroFrameTicks = frameTicks;
        _lastGyroSensorTimestamp = s.SensorTimestamp;
        if (dt <= 0f || dt > 0.05f)
        {
            _gyroAccumX = 0;
            _gyroAccumY = 0;
            return;
        }

        UpdateGravity(s, dt);

        // Player-space yaw keeps horizontal movement broad as the controller
        // tilts. Keep local roll as an independent candidate so rotating around
        // Z never gets cancelled by the gravity projection.
        float playerSpaceX = -CalculatePlayerSpaceYaw(s.GyroY, s.GyroZ);
        float rollX = -s.GyroZ * MathF.Max(0, config.ZAxisMultiplier);
        float velocityX = MathF.Abs(rollX) > MathF.Abs(playerSpaceX)
            ? rollX
            : playerSpaceX;
        float velocityY = -s.GyroX;
        float magnitude = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        float deadzone = MathF.Max(0, config.Deadzone);
        if (magnitude <= deadzone) return;

        float deadzoneScale = (magnitude - deadzone) / magnitude;
        float scale = dt * deadzoneScale;
        _gyroAccumX += velocityX * config.HorizontalSensitivity * scale;
        _gyroAccumY += velocityY * config.Sensitivity * scale;

        int dx = (int)MathF.Truncate(_gyroAccumX);
        int dy = (int)MathF.Truncate(_gyroAccumY);
        if (dx == 0 && dy == 0) return;

        InputSimulator.MoveRelative(dx, dy);
        _gyroAccumX -= dx;
        _gyroAccumY -= dy;
    }

    private void UpdateGravity(in DualSenseState s, float dt)
    {
        float magnitude = MathF.Sqrt(
            s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ);
        if (magnitude is < 0.5f or > 1.5f) return;

        // An accelerometer at rest points opposite gravity. GamepadMotionHelpers'
        // player-space formula expects the gravity vector itself.
        float x = -s.AccelX / magnitude;
        float y = -s.AccelY / magnitude;
        float z = -s.AccelZ / magnitude;
        if (!_hasGravity)
        {
            _gravityX = x;
            _gravityY = y;
            _gravityZ = z;
            _hasGravity = true;
            return;
        }

        const float GravitySmoothingSeconds = 0.25f;
        float blend = 1f - MathF.Exp(-dt / GravitySmoothingSeconds);
        _gravityX += (x - _gravityX) * blend;
        _gravityY += (y - _gravityY) * blend;
        _gravityZ += (z - _gravityZ) * blend;
        float filteredMagnitude = MathF.Sqrt(
            _gravityX * _gravityX + _gravityY * _gravityY + _gravityZ * _gravityZ);
        if (filteredMagnitude <= 0f) return;
        _gravityX /= filteredMagnitude;
        _gravityY /= filteredMagnitude;
        _gravityZ /= filteredMagnitude;
    }

    private float CalculatePlayerSpaceYaw(float gyroY, float gyroZ)
    {
        if (!_hasGravity) return gyroY;

        const float YawRelaxFactor = 1.41f;
        float worldYaw = -(_gravityY * gyroY + _gravityZ * gyroZ);
        float yawLimit = MathF.Sqrt(gyroY * gyroY + gyroZ * gyroZ);
        float magnitude = MathF.Min(MathF.Abs(worldYaw) * YawRelaxFactor, yawLimit);
        return MathF.CopySign(magnitude, worldYaw);
    }

    private bool IsGyroActive(in DualSenseState s, string activation) => activation switch
    {
        GyroActivationModes.HoldL2 => s.L2Trigger > Config.TriggerThreshold,
        GyroActivationModes.HoldR2 => s.R2Trigger > Config.TriggerThreshold,
        _ => true,
    };

    private void ResetGyroMotion()
    {
        _lastGyroFrameTicks = 0;
        _lastGyroSensorTimestamp = 0;
        _gravityX = _gravityY = _gravityZ = 0;
        _hasGravity = false;
        _gyroAccumX = 0;
        _gyroAccumY = 0;
    }

    private void ProcessTouchpad(in DualSenseState s)
    {
        if (!s.HasTouchpadData)
        {
            ResetTouchpadMotion();
        }
        else
        {
            int activeCount = (s.Touch1.Active ? 1 : 0) + (s.Touch2.Active ? 1 : 0);
            if (activeCount > 0)
            {
                _lastTouchX = activeCount == 2
                    ? (s.Touch1.X + s.Touch2.X) / 2
                    : s.Touch1.Active ? s.Touch1.X : s.Touch2.X;
                _lastTouchSeenMs = Environment.TickCount64;
            }

            if (Config.TouchpadPointerEnabled && activeCount == 1)
            {
                var touch = s.Touch1.Active ? s.Touch1 : s.Touch2;
                int slot = s.Touch1.Active ? 1 : 2;
                MovePointerFromTouch(touch, slot);
            }
            else
            {
                // Two contacts are reserved for gestures. Resetting here also
                // prevents a jump when pointer movement is re-enabled mid-touch.
                ResetTouchpadMotion();
            }
        }

        bool pressed = (s.Buttons & DualSenseButton.Touchpad) != 0;
        if (pressed && _touchpadClickRegion == TouchpadRegion.None)
            _touchpadClickRegion = ResolveTouchpadRegion();

        var mappings = Config.Mappings;
        DispatchInputEdge(mappings.TouchpadLeft,
            pressed && _touchpadClickRegion == TouchpadRegion.Left,
            ref _touchLeftPrev);
        DispatchInputEdge(mappings.TouchpadCenter,
            pressed && _touchpadClickRegion == TouchpadRegion.Center,
            ref _touchCenterPrev);
        DispatchInputEdge(mappings.TouchpadRight,
            pressed && _touchpadClickRegion == TouchpadRegion.Right,
            ref _touchRightPrev);

        if (!pressed) _touchpadClickRegion = TouchpadRegion.None;
    }

    private void MovePointerFromTouch(in TouchContact touch, int slot)
    {
        if (!_touchMoveActive || _touchMoveId != touch.Id || _touchMoveSlot != slot)
        {
            _touchMoveActive = true;
            _touchMoveId = touch.Id;
            _touchMoveSlot = slot;
            _touchMoveX = touch.X;
            _touchMoveY = touch.Y;
            _touchMoveAccumX = 0;
            _touchMoveAccumY = 0;
            return;
        }

        int rawDx = touch.X - _touchMoveX;
        int rawDy = touch.Y - _touchMoveY;
        _touchMoveX = touch.X;
        _touchMoveY = touch.Y;

        if (Math.Abs(rawDx) > TouchpadMaxDeltaPerTick || Math.Abs(rawDy) > TouchpadMaxDeltaPerTick)
        {
            _touchMoveAccumX = 0;
            _touchMoveAccumY = 0;
            return;
        }

        // Fractional accumulation damps one-unit sensor jitter while retaining
        // deliberate slow movement instead of discarding small deltas.
        float pointerScale = Config.LeftStick.Sensitivity / TouchpadSensitivityDivisor;
        _touchMoveAccumX += rawDx * pointerScale;
        _touchMoveAccumY += rawDy * pointerScale;
        int dx = (int)MathF.Truncate(_touchMoveAccumX);
        int dy = (int)MathF.Truncate(_touchMoveAccumY);
        if (dx == 0 && dy == 0) return;

        InputSimulator.MoveRelative(dx, dy);
        _touchMoveAccumX -= dx;
        _touchMoveAccumY -= dy;
    }

    private TouchpadRegion ResolveTouchpadRegion()
    {
        // A mechanical click can arrive one frame after the contact disappears.
        // Keep the last location briefly; an unlocated click falls back to center.
        int x = Environment.TickCount64 - _lastTouchSeenMs <= 150
            ? _lastTouchX
            : TouchpadWidth / 2;
        if (x < TouchpadWidth / 3) return TouchpadRegion.Left;
        if (x < TouchpadWidth * 2 / 3) return TouchpadRegion.Center;
        return TouchpadRegion.Right;
    }

    private void ResetTouchpadMotion()
    {
        _touchMoveActive = false;
        _touchMoveAccumX = 0;
        _touchMoveAccumY = 0;
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

        bool r2 = s.R2Trigger > thr;
        DispatchInputEdge(m.R2, r2, ref _r2Prev);

        bool l2 = s.L2Trigger > thr;
        DispatchInputEdge(m.L2, l2, ref _l2Prev);
    }

    private void ProcessButtons(DualSenseState s, DualSenseButton newlyPressed)
    {
        var released = _prevButtons & ~s.Buttons;
        var m = Config.Mappings;

        bool cross    = (s.Buttons & DualSenseButton.Cross)    != 0;
        bool circle   = (s.Buttons & DualSenseButton.Circle)   != 0;
        bool square   = (s.Buttons & DualSenseButton.Square)   != 0;
        bool triangle = (s.Buttons & DualSenseButton.Triangle) != 0;
        bool l3       = (s.Buttons & DualSenseButton.L3)       != 0;
        bool r3       = (s.Buttons & DualSenseButton.R3)       != 0;

        // L3+R3 fires the Enabled-toggle combo, which would otherwise also
        // trigger the per-button L3 / R3 mappings. Suppress those while the
        // combo is engaged; stay sticky until both are released so partially
        // lifting one stick can't manufacture a fresh rising edge.
        if (l3 && r3) _l3r3ComboActive = true;
        else if (!l3 && !r3) _l3r3ComboActive = false;

        DispatchInputEdge(m.Cross,    cross,    ref _crossPrev);
        DispatchInputEdge(m.Circle,   circle,   ref _circlePrev);
        DispatchInputEdge(m.Square,   square,   ref _squarePrev);
        DispatchInputEdge(m.Triangle, triangle, ref _trianglePrev);
        DispatchInputEdge(m.L3,       l3 && !_l3r3ComboActive, ref _l3Prev);
        DispatchInputEdge(m.R3,       r3 && !_l3r3ComboActive, ref _r3Prev);

        // D-Pad → arrow keys (not user-configurable for now).
        HoldKey(newlyPressed, released, DualSenseButton.DPadUp,    VK_UP);
        HoldKey(newlyPressed, released, DualSenseButton.DPadDown,  VK_DOWN);
        HoldKey(newlyPressed, released, DualSenseButton.DPadLeft,  VK_LEFT);
        HoldKey(newlyPressed, released, DualSenseButton.DPadRight, VK_RIGHT);
    }

    private void ProcessSystemShortcuts(DualSenseState s)
    {
        // L1+R1 → toggle our in-process on-screen keyboard. Hardcoded combo,
        // not user-configurable yet — neither L1 nor R1 maps to anything else,
        // so there is no conflict with the per-button mappings.
        const DualSenseButton KeyboardCombo = DualSenseButton.L1 | DualSenseButton.R1;
        bool kbNow  = (s.Buttons    & KeyboardCombo) == KeyboardCombo;
        bool kbPrev = (_prevButtons & KeyboardCombo) == KeyboardCombo;
        if (kbNow && !kbPrev)
        {
            OnSystemKeyboardToggle();
        }

        // Share+Options (the two small buttons either side of the touchpad)
        // → snap the cursor to the center of the primary display. Useful when
        // the cursor has drifted off-screen on a multi-monitor setup.
        const DualSenseButton CenterCombo = DualSenseButton.Share | DualSenseButton.Options;
        bool centerNow  = (s.Buttons    & CenterCombo) == CenterCombo;
        bool centerPrev = (_prevButtons & CenterCombo) == CenterCombo;
        if (centerNow && !centerPrev)
        {
            InputSimulator.CenterCursorOnPrimary();
        }
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
        if (ButtonActions.TryParseKey(action, out var keyVk))
        {
            InputSimulator.KeyDown(keyVk);
            return;
        }
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
            case ButtonActions.Tab:         InputSimulator.KeyTap(VK_TAB);                 break;
            case ButtonActions.Backspace:   InputSimulator.KeyTap(VK_BACK);                break;
            case ButtonActions.Ctrl:        InputSimulator.KeyDown(VK_CONTROL);            break;
            case ButtonActions.Shift:       InputSimulator.KeyDown(VK_SHIFT);              break;
            case ButtonActions.Alt:         InputSimulator.KeyDown(VK_MENU);               break;
            // None / unknown: no-op
        }
    }

    private static void ApplyUp(string action)
    {
        if (ButtonActions.TryParseKey(action, out var keyVk))
        {
            InputSimulator.KeyUp(keyVk);
            return;
        }
        switch (action)
        {
            case ButtonActions.LeftHold:   InputSimulator.MouseUp(MouseButton.Left);   break;
            case ButtonActions.RightHold:  InputSimulator.MouseUp(MouseButton.Right);  break;
            case ButtonActions.MiddleHold: InputSimulator.MouseUp(MouseButton.Middle); break;
            case ButtonActions.Ctrl:       InputSimulator.KeyUp(VK_CONTROL);           break;
            case ButtonActions.Shift:      InputSimulator.KeyUp(VK_SHIFT);             break;
            case ButtonActions.Alt:        InputSimulator.KeyUp(VK_MENU);              break;
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
        if (_r2Prev)       ApplyUp(m.R2);
        if (_crossPrev)    ApplyUp(m.Cross);
        if (_l2Prev)       ApplyUp(m.L2);
        if (_circlePrev)   ApplyUp(m.Circle);
        if (_squarePrev)   ApplyUp(m.Square);
        if (_trianglePrev) ApplyUp(m.Triangle);
        if (_l3Prev)       ApplyUp(m.L3);
        if (_r3Prev)       ApplyUp(m.R3);
        if (_touchLeftPrev)   ApplyUp(m.TouchpadLeft);
        if (_touchCenterPrev) ApplyUp(m.TouchpadCenter);
        if (_touchRightPrev)  ApplyUp(m.TouchpadRight);
        _r2Prev = _crossPrev = _l2Prev = _circlePrev = _squarePrev = _trianglePrev = _l3Prev = _r3Prev = false;
        _touchLeftPrev = _touchCenterPrev = _touchRightPrev = false;
        _touchpadClickRegion = TouchpadRegion.None;
        _lastTouchSeenMs = 0;
        ResetTouchpadMotion();
        ResetGyroMotion();

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
