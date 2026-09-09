namespace TaskbarBanner.Core.Reporting;

public sealed class MinuteUploader
{
    public const int BatchSize = 50;
    public static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(5);

    private readonly IMinuteStore _store;
    private readonly IReportApiClient _api;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private TimeSpan _currentBackoff;

    public MinuteUploader(
        IMinuteStore store,
        IReportApiClient api,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
        _maxBackoff = maxBackoff ?? DefaultMaxBackoff;
        _currentBackoff = _initialBackoff;
    }

    public event EventHandler<UploadAttemptEventArgs>? AttemptCompleted;

    public TimeSpan CurrentBackoff => _currentBackoff;

    public bool IsAttemptInProgress => _singleFlight.CurrentCount == 0;

    public async Task<int> TryUploadPendingAsync(CancellationToken cancellationToken)
    {
        if (!await _singleFlight.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        try
        {
            IReadOnlyList<VerifiedMinute> batch = _store.GetPending(BatchSize);
            if (batch.Count == 0)
            {
                return 0;
            }

            bool succeeded;
            try
            {
                succeeded = await _api.TryUploadAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                succeeded = false;
            }

            int uploaded = 0;
            if (succeeded)
            {
                _store.MarkUploaded(batch.Select(m => m.Id).ToList());
                uploaded = batch.Count;
                _currentBackoff = _initialBackoff;
            }
            else
            {
                _store.MarkAttemptFailed(batch.Select(m => m.Id).ToList());
                _currentBackoff = _currentBackoff >= _maxBackoff
                    ? _maxBackoff
                    : TimeSpan.FromTicks(Math.Min(_maxBackoff.Ticks, _currentBackoff.Ticks * 2));
            }

            AttemptCompleted?.Invoke(this, new UploadAttemptEventArgs(uploaded, succeeded));
            return uploaded;
        }
        finally
        {
            _singleFlight.Release();
        }
    }
}
