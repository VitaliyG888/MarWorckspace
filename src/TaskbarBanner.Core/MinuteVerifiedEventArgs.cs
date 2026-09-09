namespace TaskbarBanner.Core;

public sealed class MinuteVerifiedEventArgs : EventArgs
{
    public required DateTimeOffset MinuteStartUtc { get; init; }
    public required DateTimeOffset MinuteEndUtc { get; init; }
}
