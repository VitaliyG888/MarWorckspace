namespace TaskbarBanner.Core.Reporting;

public sealed record MinutePayload(
    string IdempotencyKey,
    string SessionId,
    string DeviceId,
    DateTimeOffset MinuteStartUtc,
    DateTimeOffset MinuteEndUtc);

public sealed record ReportPayload(IReadOnlyList<MinutePayload> Minutes);

public static class ReportPayloadFactory
{
    public static ReportPayload Build(IReadOnlyList<VerifiedMinute> batch)
        => new(batch
            .Select(m => new MinutePayload(
                m.IdempotencyKey,
                m.SessionId,
                m.DeviceId,
                m.MinuteStartUtc,
                m.MinuteEndUtc))
            .ToList());
}
