namespace MotionLinkVR.Core;

public static class AngleMath
{
    public const double MaxDegrees = 180d;
    public const double MinDegrees = -180d;

    public static double NormalizeDegrees(double value)
    {
        var normalized = value % 360d;
        if (normalized > 180d) normalized -= 360d;
        if (normalized < -180d) normalized += 360d;
        return normalized;
    }

    public static double AngleDeltaDegrees(double from, double to)
    {
        var delta = NormalizeDegrees(to - from);
        return delta;
    }

    public static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
