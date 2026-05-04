using DS2Mouse.Models;

namespace DS2Mouse.Services;

/// <summary>
/// Combines multiple <see cref="DualSenseState"/> snapshots from different
/// physical controllers into one. Sticks/triggers take the larger-magnitude
/// input so opposing pushes don't cancel; buttons OR together so any pad
/// pressing the button is enough to trigger the mapped action.
/// </summary>
internal static class StateMerge
{
    public static DualSenseState Combine(DualSenseState a, DualSenseState b)
    {
        var (lx, ly) = MagMax(a.LeftStickX,  a.LeftStickY,  b.LeftStickX,  b.LeftStickY);
        var (rx, ry) = MagMax(a.RightStickX, a.RightStickY, b.RightStickX, b.RightStickY);
        return new DualSenseState(
            lx, ly, rx, ry,
            MathF.Max(a.L2Trigger, b.L2Trigger),
            MathF.Max(a.R2Trigger, b.R2Trigger),
            a.Buttons | b.Buttons,
            Math.Max(a.TimestampTicks, b.TimestampTicks));
    }

    private static (float x, float y) MagMax(float ax, float ay, float bx, float by)
        => (ax * ax + ay * ay) >= (bx * bx + by * by) ? (ax, ay) : (bx, by);
}
