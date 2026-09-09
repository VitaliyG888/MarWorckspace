namespace TaskbarBanner.App.Ads;

public interface IAdSource
{
    AdContent GetCurrent();

    Task RefreshAsync(CancellationToken cancellationToken);
}
