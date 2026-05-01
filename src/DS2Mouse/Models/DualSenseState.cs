namespace DS2Mouse.Models;

[Flags]
public enum DualSenseButton : uint
{
    None        = 0,
    Square      = 1 << 0,
    Cross       = 1 << 1,
    Circle      = 1 << 2,
    Triangle    = 1 << 3,
    L1          = 1 << 4,
    R1          = 1 << 5,
    L2          = 1 << 6,
    R2          = 1 << 7,
    Share       = 1 << 8,
    Options     = 1 << 9,
    L3          = 1 << 10,
    R3          = 1 << 11,
    PS          = 1 << 12,
    Touchpad    = 1 << 13,
    Mute        = 1 << 14,
    DPadUp      = 1 << 16,
    DPadRight   = 1 << 17,
    DPadDown    = 1 << 18,
    DPadLeft    = 1 << 19,
}

public enum ConnectionType
{
    Disconnected,
    Usb,
    Bluetooth,
}

public readonly record struct DualSenseState(
    float LeftStickX,   // -1.0 .. 1.0
    float LeftStickY,   // -1.0 .. 1.0  (positive = up)
    float RightStickX,
    float RightStickY,
    float L2Trigger,    // 0 .. 1
    float R2Trigger,    // 0 .. 1
    DualSenseButton Buttons,
    long TimestampTicks)
{
    public bool IsPressed(DualSenseButton b) => (Buttons & b) == b;
}
