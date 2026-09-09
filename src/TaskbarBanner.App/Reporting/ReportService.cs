using TaskbarBanner.Core.Reporting;

namespace TaskbarBanner.App.Reporting;

public sealed class ReportService : IDisposable
{
    private readonly IMinuteStore _store;
    private readonly MinuteUploader _uploader;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private readonly TimeSpan _pollInterval;
    private string _sessionId;

    public ReportService(string databasePath, IReportApiClient api, TimeSpan? pollInterval = null)
    {
        _store = SqliteMinuteStore.Open(databasePath);
        _uploader = new MinuteUploader(_store, api);
        _sessionId = Guid.NewGuid().ToString("N");
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public IMinuteStore Store => _store;

    public MinuteUploader Uploader => _uploader;

    public string SessionId => _sessionId;

    public string DeviceId => _store.DeviceId;

    public void RecordVerifiedMinute(DateTimeOffset minuteStartUtc, DateTimeOffset minuteEndUtc)
    {
        _store.Enqueue(new VerifiedMinute
        {
            IdempotencyKey = $"min-{DeviceId}-{minuteStartUtc:yyyyMMdd'T'HHmmss'Z'}",
            SessionId = _sessionId,
            DeviceId = DeviceId,
            MinuteStartUtc = minuteStartUtc,
            MinuteEndUtc = minuteEndUtc,
            CreatedUtc = DateTimeOffset.UtcNow,
        });
    }

    public void ResetSession()
    {
        _store.ClearPending();
        _sessionId = Guid.NewGuid().ToString("N");
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _pump.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // best-effort shutdown
        }

        _cts.Dispose();
        _store.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait = _pollInterval;
            try
            {
                long pendingBefore = _store.CountPending();
                int uploaded = await _uploader.TryUploadPendingAsync(cancellationToken).ConfigureAwait(false);
                if (uploaded > 0)
                {
                    wait = _pollInterval;
                }
                else if (pendingBefore > 0)
                {
                    wait = _uploader.CurrentBackoff;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                wait = _uploader.CurrentBackoff;
            }

            try
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
