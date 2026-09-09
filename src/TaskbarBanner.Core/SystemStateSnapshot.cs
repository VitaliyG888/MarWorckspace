namespace TaskbarBanner.Core;

public readonly record struct SystemStateSnapshot(
    DateTimeOffset TimestampUtc,
    bool IsPoweredOn,
    bool IsUnlocked,
    bool IsSessionConnected)
{
    public bool MeetsBaseline => IsPoweredOn && IsUnlocked && IsSessionConnected;
}
