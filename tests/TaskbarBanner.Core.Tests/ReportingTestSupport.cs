using TaskbarBanner.Core.Reporting;

namespace TaskbarBanner.Core.Tests;

internal static class MinuteFactory
{
    public static VerifiedMinute Make(string idempotencyKey, DateTimeOffset startUtc)
        => new()
        {
            IdempotencyKey = idempotencyKey,
            SessionId = "session-1",
            DeviceId = "device-1",
            MinuteStartUtc = startUtc,
            MinuteEndUtc = startUtc.AddMinutes(1),
            CreatedUtc = DateTimeOffset.UtcNow,
        };
}

internal sealed class FakeReportApiClient : IReportApiClient
{
    private readonly List<bool> _results;
    private int _index;

    public FakeReportApiClient(params bool[] results)
    {
        _results = results.ToList();
    }

    public List<IReadOnlyList<VerifiedMinute>> Batches { get; } = new();

    public Task<bool> TryUploadAsync(IReadOnlyList<VerifiedMinute> batch, CancellationToken cancellationToken)
    {
        Batches.Add(batch.ToList());
        bool result = _index < _results.Count ? _results[_index++] : true;
        return Task.FromResult(result);
    }
}
