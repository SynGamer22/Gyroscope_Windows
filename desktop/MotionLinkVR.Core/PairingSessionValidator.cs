namespace MotionLinkVR.Core;

public static class PairingSessionValidator
{
    public static bool IsValid(PairingSession session)
    {
        if (string.IsNullOrWhiteSpace(session.Code)) return false;
        if (string.IsNullOrWhiteSpace(session.OwnerId)) return false;
        if (!session.IsActive) return false;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow) return false;
        return true;
    }

    public static bool IsExpired(string code, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        return expiresAt <= DateTimeOffset.UtcNow;
    }
}
