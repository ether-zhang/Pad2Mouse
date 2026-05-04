namespace DS2Mouse.Models;

[Flags]
public enum ControllerKind
{
    None      = 0,
    DualSense = 1 << 0,
    Xbox      = 1 << 1,
}
