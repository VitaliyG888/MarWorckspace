using TaskbarBanner.Core.Reporting;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class MinuteUploaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessfulUpload_MarksAllAsUploaded_AndResetsBackoff()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();
        store.Enqueue(MinuteFactory.Make("a", Start));
        store.Enqueue(MinuteFactory.Make("b", Start.AddMinutes(1)));

        FakeReportApiClient api = new(true);
        MinuteUploader uploader = new(store, api, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));

        int uploaded = await uploader.TryUploadPendingAsync(CancellationToken.None);

        Assert.Equal(2, uploaded);
        Assert.Equal(0, store.CountPending());
        Assert.Equal(2, store.CountUploaded());
        Assert.Equal(TimeSpan.FromSeconds(1), uploader.CurrentBackoff);
        Assert.Single(api.Batches);
        Assert.Equal(new[] { "a", "b" }, api.Batches[0].Select(m => m.IdempotencyKey));
    }

    [Fact]
    public async Task RepeatedFailures_KeepRowsPending_GrowBackoff_ThenRecover()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();
        store.Enqueue(MinuteFactory.Make("a", Start));
        store.Enqueue(MinuteFactory.Make("b", Start.AddMinutes(1)));

        FakeReportApiClient api = new(false, false, true);
        MinuteUploader uploader = new(store, api, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));

        int first = await uploader.TryUploadPendingAsync(CancellationToken.None);
        Assert.Equal(0, first);
        Assert.Equal(2, store.CountPending());
        Assert.All(store.GetPending(10), m => Assert.Equal(1, m.AttemptCount));
        Assert.Equal(TimeSpan.FromSeconds(2), uploader.CurrentBackoff);

        int second = await uploader.TryUploadPendingAsync(CancellationToken.None);
        Assert.Equal(0, second);
        Assert.Equal(TimeSpan.FromSeconds(4), uploader.CurrentBackoff);
        Assert.All(store.GetPending(10), m => Assert.Equal(2, m.AttemptCount));

        int third = await uploader.TryUploadPendingAsync(CancellationToken.None);
        Assert.Equal(2, third);
        Assert.Equal(0, store.CountPending());
        Assert.Equal(TimeSpan.FromSeconds(1), uploader.CurrentBackoff);
    }

    [Fact]
    public async Task RetryResendsTheSameIdempotencyKeys()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();
        store.Enqueue(MinuteFactory.Make("key-1", Start));
        store.Enqueue(MinuteFactory.Make("key-2", Start.AddMinutes(1)));

        FakeReportApiClient api = new(false, true);
        MinuteUploader uploader = new(store, api);

        await uploader.TryUploadPendingAsync(CancellationToken.None);
        await uploader.TryUploadPendingAsync(CancellationToken.None);

        Assert.Equal(2, api.Batches.Count);
        Assert.Equal(
            new[] { "key-1", "key-2" },
            api.Batches[0].Select(m => m.IdempotencyKey));
        Assert.Equal(
            new[] { "key-1", "key-2" },
            api.Batches[1].Select(m => m.IdempotencyKey));
        Assert.Equal(2, store.CountUploaded());
        Assert.Equal(0, store.CountPending());
    }

    [Fact]
    public async Task SingleFlight_PreventsConcurrentDoubleSend()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();
        store.Enqueue(MinuteFactory.Make("a", Start));
        store.Enqueue(MinuteFactory.Make("b", Start.AddMinutes(1)));

        var api = new BlockingReportApiClient();
        MinuteUploader uploader = new(store, api);

        Task<int> first = uploader.TryUploadPendingAsync(CancellationToken.None);
        await api.Started.Task;

        int skipped = await uploader.TryUploadPendingAsync(CancellationToken.None);
        Assert.Equal(0, skipped);

        api.Release.SetResult();
        int uploaded = await first;

        Assert.Equal(2, uploaded);
        Assert.Single(api.Batches);
        Assert.Equal(2, store.CountUploaded());
    }

    [Fact]
    public async Task NoPendingRows_DoesNotCallApi()
    {
        using TempDatabase db = new();
        using IMinuteStore store = db.Open();

        FakeReportApiClient api = new(true);
        MinuteUploader uploader = new(store, api);

        int uploaded = await uploader.TryUploadPendingAsync(CancellationToken.None);

        Assert.Equal(0, uploaded);
        Assert.Empty(api.Batches);
    }

    private sealed class BlockingReportApiClient : IReportApiClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IReadOnlyList<VerifiedMinute>> Batches { get; } = new();

        public async Task<bool> TryUploadAsync(IReadOnlyList<VerifiedMinute> batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch.ToList());
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return true;
        }
    }
}
