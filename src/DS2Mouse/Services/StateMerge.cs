using System.Diagnostics;
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
        var touch = SelectTouchpad(a, b);
        var gyro = SelectGyro(a, b);
        return new DualSenseState(
            lx, ly, rx, ry,
            MathF.Max(a.L2Trigger, b.L2Trigger),
            MathF.Max(a.R2Trigger, b.R2Trigger),
            a.Buttons | b.Buttons,
            touch.HasTouchpadData,
            touch.Touch1,
            touch.Touch2,
            gyro.HasGyroData,
            gyro.GyroX,
            gyro.GyroY,
            gyro.GyroZ,
            gyro.AccelX,
            gyro.AccelY,
            gyro.AccelZ,
            gyro.SensorTimestamp,
            gyro.GyroTimestampTicks,
            Math.Max(a.TimestampTicks, b.TimestampTicks));
    }

    private static DualSenseState SelectGyro(DualSenseState a, DualSenseState b)
    {
        if (!a.HasGyroData) return b;
        if (!b.HasGyroData) return a;
        return a.GyroTimestampTicks >= b.GyroTimestampTicks ? a : b;
    }

    private static DualSenseState SelectTouchpad(DualSenseState a, DualSenseState b)
    {
        if (!a.HasTouchpadData) return b;
        if (!b.HasTouchpadData) return a;

        bool aActive = a.Touch1.Active || a.Touch2.Active;
        bool bActive = b.Touch1.Active || b.Touch2.Active;
        if (aActive != bActive)
        {
            var active = aActive ? a : b;
            var newest = a.TimestampTicks >= b.TimestampTicks ? a : b;
            // Keep a live contact from being replaced by another controller's
            // slightly newer idle frame, but do not preserve a stale contact.
            if (newest.TimestampTicks - active.TimestampTicks <= Stopwatch.Frequency / 10)
                return active;
        }

        return a.TimestampTicks >= b.TimestampTicks ? a : b;
    }

    private static (float x, float y) MagMax(float ax, float ay, float bx, float by)
        => (ax * ax + ay * ay) >= (bx * bx + by * by) ? (ax, ay) : (bx, by);
}
