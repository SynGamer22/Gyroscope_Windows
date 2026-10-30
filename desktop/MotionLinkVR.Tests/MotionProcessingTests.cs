using MotionLinkVR.Core;
using Xunit;

public class MotionProcessingTests
{
    [Fact]
    public void NormalizeDegrees_HandlesWrapping()
    {
        Assert.Equal(-90d, AngleMath.NormalizeDegrees(270d));
        Assert.Equal(179d, AngleMath.NormalizeDegrees(-181d));
        Assert.Equal(0d, AngleMath.NormalizeDegrees(360d));
    }

    [Fact]
    public void AngleDelta_UsesShortestRoundedPath()
    {
        Assert.Equal(-10d, AngleMath.AngleDeltaDegrees(10d, 0d));
        Assert.Equal(10d, AngleMath.AngleDeltaDegrees(-10d, 0d));
        Assert.Equal(20d, AngleMath.AngleDeltaDegrees(170d, -170d));
    }

    [Fact]
    public void Recenter_UsesCurrentOrientationAsNeutralReference()
    {
        var processor = new MotionProcessor { Sensitivity = 1.0 };
        processor.Recenter(30d, 20d, 10d);

        var sample = new OrientationSample(35d, 22d, 12d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var delta = processor.Process(sample);

        Assert.True(Math.Abs(delta.X) > 0d);
        Assert.True(Math.Abs(delta.Y) > 0d);
    }

    [Fact]
    public void Sensitivity_ScalesMouseDelta()
    {
        var processor = new MotionProcessor { Sensitivity = 2d, Deadzone = 0d, Smoothing = 0d };
        processor.Recenter(0d, 0d, 0d);

        var sample = new OrientationSample(10d, 0d, 0d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var delta = processor.Process(sample);

        Assert.True(delta.X > 0d);
        Assert.Equal(20d, Math.Round(delta.X, 1), 1);
    }

    [Fact]
    public void Deadzone_IgnoresSmallMovement()
    {
        var processor = new MotionProcessor { Sensitivity = 1d, Deadzone = 2d, Smoothing = 0d };
        processor.Recenter(0d, 0d, 0d);

        var sample = new OrientationSample(1d, 0d, 0d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var delta = processor.Process(sample);

        Assert.Equal(0d, delta.X, 10);
    }

    [Fact]
    public void Smoothing_ReducesJitter()
    {
        var processor = new MotionProcessor { Sensitivity = 1d, Deadzone = 0d, Smoothing = 0.5d };
        processor.Recenter(0d, 0d, 0d);

        var first = processor.Process(new OrientationSample(10d, 0d, 0d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var second = processor.Process(new OrientationSample(10d, 0d, 0d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        Assert.True(Math.Abs(first.X) >= Math.Abs(second.X));
    }

    [Fact]
    public void InvalidSensorData_IsRejected()
    {
        var processor = new MotionProcessor { Enabled = true };
        var invalid = new OrientationSample(double.NaN, 0d, 0d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
        var delta = processor.Process(invalid);

        Assert.False(delta.IsValid);
    }

    [Fact]
    public void StalePacket_IsRejected()
    {
        var processor = new MotionProcessor();

        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        Assert.True(processor.IsStale(staleTimestamp));
    }

    [Fact]
    public void MouseDelta_Conversion_UsesRelativeMovement()
    {
        var processor = new MotionProcessor { Sensitivity = 1d, Deadzone = 0d, Smoothing = 0d, MaxMovementRate = 100d };
        processor.Recenter(0d, 0d, 0d);

        var delta = processor.Process(new OrientationSample(5d, -6d, 0d, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        Assert.True(delta.X != 0d || delta.Y != 0d);
        Assert.True(Math.Abs(delta.X) <= 100d);
        Assert.True(Math.Abs(delta.Y) <= 100d);
    }

    [Fact]
    public void PairingSessionValidator_RejectsExpiredSessions()
    {
        var session = new PairingSession("ABCD", "owner", DateTimeOffset.UtcNow.AddMinutes(-5), true);
        Assert.False(PairingSessionValidator.IsValid(session));
    }
}
