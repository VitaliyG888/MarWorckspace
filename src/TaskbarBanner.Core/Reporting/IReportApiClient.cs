namespace TaskbarBanner.Core.Reporting;

public interface IReportApiClient
{
    Task<bool> TryUploadAsync(IReadOnlyList<VerifiedMinute> batch, CancellationToken cancellationToken);
}
