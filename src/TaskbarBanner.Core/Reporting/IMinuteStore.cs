namespace TaskbarBanner.Core.Reporting;

public interface IMinuteStore : IDisposable
{
    string DeviceId { get; }

    void Enqueue(VerifiedMinute minute);

    IReadOnlyList<VerifiedMinute> GetPending(int limit);

    void MarkUploaded(IReadOnlyList<long> ids);

    void MarkAttemptFailed(IReadOnlyList<long> ids);

    void ClearPending();

    long CountPending();

    long CountUploaded();

    long CountAll();
}
