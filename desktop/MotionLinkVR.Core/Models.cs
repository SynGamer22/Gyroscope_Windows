namespace MotionLinkVR.Core;

public readonly record struct OrientationSample(
    double Yaw,
    double Pitch,
    double Roll,
    long TimestampUtc,
    bool IsValid = true);

public readonly record struct MouseDelta(
    double X,
    double Y,
    bool IsValid = true);

public readonly record struct PairingSession(
    string Code,
    string OwnerId,
    DateTimeOffset ExpiresAt,
    bool IsActive,
    string? State = null);
