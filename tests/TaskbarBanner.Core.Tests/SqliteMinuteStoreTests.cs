using TaskbarBanner.Core.Reporting;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class SqliteMinuteStoreTests
{
    [Fact]
    public void EnqueueAndGetPending_ReturnsRowsOldestFirst()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();

        DateTimeOffset start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        store.Enqueue(MinuteFactory.Make("a", start));
        store.Enqueue(MinuteFactory.Make("b", start.AddMinutes(1)));

        IReadOnlyList<VerifiedMinute> pending = store.GetPending(10);

        Assert.Equal(2, pending.Count);
        Assert.Equal("a", pending[0].IdempotencyKey);
        Assert.Equal("b", pending[1].IdempotencyKey);
        Assert.Equal(start, pending[0].MinuteStartUtc);
        Assert.Equal(MinuteReportState.Pending, pending[0].State);
        Assert.Equal(2, store.CountPending());
        Assert.Equal(0, store.CountUploaded());
    }

    [Fact]
    public void DuplicateIdempotencyKey_IsIgnored()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();

        DateTimeOffset start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        store.Enqueue(MinuteFactory.Make("same-key", start));
        store.Enqueue(MinuteFactory.Make("same-key", start.AddMinutes(1)));

        Assert.Equal(1, store.CountAll());
    }

    [Fact]
    public void DeviceId_IsStableAcrossReopen()
    {
        using TempDatabase db = new();
        using (IMinuteStore first = db.Open())
        {
            Assert.False(string.IsNullOrWhiteSpace(first.DeviceId));
            using IMinuteStore second = db.Open();
            Assert.Equal(first.DeviceId, second.DeviceId);
        }
    }

    [Fact]
    public void MarkUploaded_MovesRowsOutOfPendingQueue()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();

        store.Enqueue(MinuteFactory.Make("a", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
        store.Enqueue(MinuteFactory.Make("b", new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero)));
        IReadOnlyList<VerifiedMinute> pending = store.GetPending(10);

        store.MarkUploaded(new[] { pending[0].Id });

        Assert.Equal(1, store.CountPending());
        Assert.Equal(1, store.CountUploaded());
        Assert.Equal("b", store.GetPending(10)[0].IdempotencyKey);
    }

    [Fact]
    public void MarkAttemptFailed_IncrementsAttemptCount_AndPersists()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();

        store.Enqueue(MinuteFactory.Make("a", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
        IReadOnlyList<VerifiedMinute> pending = store.GetPending(10);

        store.MarkAttemptFailed(new[] { pending[0].Id });

        VerifiedMinute after = store.GetPending(10)[0];
        Assert.Equal(1, after.AttemptCount);
        Assert.NotNull(after.LastAttemptUtc);
        Assert.Equal(MinuteReportState.Pending, after.State);
        Assert.Equal(1, store.CountPending());
    }

    [Fact]
    public void PendingRows_SurviveStoreReopen()
    {
        using TempDatabase db = new();
        using (IMinuteStore first = db.Open())
        {
            first.Enqueue(MinuteFactory.Make("a", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
        }

        using IMinuteStore second = db.Open();
        Assert.Equal(1, second.CountPending());
        Assert.Equal("a", second.GetPending(10)[0].IdempotencyKey);
    }
}
