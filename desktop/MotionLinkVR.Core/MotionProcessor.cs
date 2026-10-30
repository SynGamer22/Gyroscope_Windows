namespace MotionLinkVR.Core;

public sealed class MotionProcessor
{
    public MotionProcessor()
    {
        Reset();
    }

    public double Sensitivity { get; set; } = 1.0;
    public double Smoothing { get; set; } = 0.25;
    public double Deadzone { get; set; } = 0.5;
    public double MaxMovementRate { get; set; } = 80.0;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    public bool Enabled { get; set; } = true;

    public double RecenterYaw { get; private set; }
    public double RecenterPitch { get; private set; }
    public double RecenterRoll { get; private set; }

    private double _targetYaw;
    private double _targetPitch;
    private double _filteredYaw;
    private double _filteredPitch;
    private double _filteredRoll;
    private double _lastSampleTimeUtc;

    public void Reset()
    {
        RecenterYaw = 0d;
        RecenterPitch = 0d;
        RecenterRoll = 0d;
        _targetYaw = 0d;
        _targetPitch = 0d;
        _filteredYaw = 0d;
        _filteredPitch = 0d;
        _filteredRoll = 0d;
        _lastSampleTimeUtc = 0d;
    }

    public void Recenter(double yaw, double pitch, double roll)
    {
        RecenterYaw = AngleMath.NormalizeDegrees(yaw);
        RecenterPitch = AngleMath.NormalizeDegrees(pitch);
        RecenterRoll = AngleMath.NormalizeDegrees(roll);
        _targetYaw = RecenterYaw;
        _targetPitch = RecenterPitch;
        _filteredYaw = RecenterYaw;
        _filteredPitch = RecenterPitch;
        _filteredRoll = RecenterRoll;
    }

    public OrientationSample NormalizeSample(OrientationSample sample)
    {
        if (!sample.IsValid)
        {
            return sample;
        }

        var yaw = AngleMath.NormalizeDegrees(sample.Yaw);
        var pitch = AngleMath.NormalizeDegrees(sample.Pitch);
        var roll = AngleMath.NormalizeDegrees(sample.Roll);

        if (!AngleMath.IsFinite(yaw) || !AngleMath.IsFinite(pitch) || !AngleMath.IsFinite(roll))
        {
            return new OrientationSample(0, 0, 0, sample.TimestampUtc, false);
        }

        return new OrientationSample(yaw, pitch, roll, sample.TimestampUtc, true);
    }

    public MouseDelta Process(OrientationSample sample)
    {
        if (!Enabled || !sample.IsValid)
        {
            return new MouseDelta(0, 0, false);
        }

        var normalized = NormalizeSample(sample);
        var t = normalized.TimestampUtc;

        if (t <= 0)
        {
            t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        var yawDelta = AngleMath.AngleDeltaDegrees(_targetYaw, normalized.Yaw);
        var pitchDelta = AngleMath.AngleDeltaDegrees(_targetPitch, normalized.Pitch);

        if (Math.Abs(yawDelta) < Deadzone) yawDelta = 0d;
        if (Math.Abs(pitchDelta) < Deadzone) pitchDelta = 0d;

        var yaw = yawDelta * Sensitivity;
        var pitch = pitchDelta * Sensitivity;

        var smoothFactor = 1d - Clamp(Smoothing, 0d, 1d);
        _filteredYaw = Lerp(_filteredYaw, yaw, smoothFactor);
        _filteredPitch = Lerp(_filteredPitch, pitch, smoothFactor);

        var finalYaw = Clamp(_filteredYaw, -MaxMovementRate, MaxMovementRate);
        var finalPitch = Clamp(_filteredPitch, -MaxMovementRate, MaxMovementRate);

        if (InvertX) finalYaw *= -1d;
        if (InvertY) finalPitch *= -1d;

        _targetYaw = normalized.Yaw;
        _targetPitch = normalized.Pitch;
        _lastSampleTimeUtc = t;

        return new MouseDelta(finalYaw, finalPitch, true);
    }

    public bool IsStale(long timestampUtc, long maxAgeMs = 250)
    {
        if (timestampUtc <= 0) return true;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return now - timestampUtc > maxAgeMs;
    }

    private static double Lerp(double from, double to, double amount)
    {
        if (double.IsNaN(from) || double.IsNaN(to)) return 0d;
        return from + (to - from) * Clamp(amount, 0d, 1d);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return min;
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
