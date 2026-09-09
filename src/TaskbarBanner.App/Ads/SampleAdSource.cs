namespace TaskbarBanner.App.Ads;

public sealed class SampleAdSource : IAdSource
{
    private static readonly TimeSpan RotationPeriod = TimeSpan.FromSeconds(20);

    private static readonly AdContent[] Ads =
    {
        new("ExampleCo", "Earn while you work", "Verified active minutes", "EX", null),
        new("SampleShop", "Try our new bundle", "Limited time offer", "SS", null),
        new("DemoBank", "0% fee checking", "Open an account today", "DB", null),
    };

    public AdContent GetCurrent()
    {
        int index = (int)(DateTime.UtcNow.Ticks / RotationPeriod.Ticks % Ads.Length);
        return Ads[index];
    }

    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
