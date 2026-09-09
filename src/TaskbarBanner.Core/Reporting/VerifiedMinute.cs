namespace TaskbarBanner.Core.Reporting;

public sealed record VerifiedMinute
{
    public long Id { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string SessionId { get; init; }
    public required string DeviceId { get; init; }
    public required DateTimeOffset MinuteStartUtc { get; init; }
    public required DateTimeOffset MinuteEndUtc { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public MinuteReportState State { get; init; } = MinuteReportState.Pending;
    public int AttemptCount { get; init; }
    public DateTimeOffset? LastAttemptUtc { get; init; }
}
