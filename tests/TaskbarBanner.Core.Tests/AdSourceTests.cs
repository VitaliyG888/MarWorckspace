using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TaskbarBanner.App.Ads;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class AdSourceTests
{
    private static readonly AdContent Fallback = new("Fallback", "Sample", "offline", "FB", null);

    private sealed class FakeAdsClient : IAdsApiClient
    {
        private readonly AdFetchResult?[] _results;
        private int _index;
        public int Calls { get; private set; }

        public FakeAdsClient(params AdFetchResult?[] results)
        {
            _results = results;
        }

        public Task<AdFetchResult?> FetchCurrentAsync(CancellationToken cancellationToken)
        {
            Calls++;
            AdFetchResult? result = _results.Length == 0
                ? null
                : _index < _results.Length
                    ? _results[_index++]
                    : _results[^1];
            return Task.FromResult(result);
        }
    }

    private static AdFetchResult Result(string name, TimeSpan rotation)
        => new(new AdContent(name, $"{name} headline", $"{name} tagline", name[..1], "https://example.com/" + name), rotation);

    private static string TempCachePath()
        => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tbb-ads-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task SuccessfulFetch_UpdatesCurrentAndWritesCache()
    {
        string path = TempCachePath();
        try
        {
            using var source = new RemoteAdSource(new FakeAdsClient(Result("AdOne", TimeSpan.FromSeconds(30))), path, Fallback);
            Assert.Equal(Fallback, source.GetCurrent());

            await source.RefreshAsync(CancellationToken.None);

            Assert.Equal("AdOne", source.GetCurrent().BrandName);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FailedFetch_KeepsPreviousAd()
    {
        string path = TempCachePath();
        try
        {
            using var source = new RemoteAdSource(new FakeAdsClient(Result("AdOne", TimeSpan.FromSeconds(30)), null), path, Fallback);

            await source.RefreshAsync(CancellationToken.None);
            await source.RefreshAsync(CancellationToken.None);

            Assert.Equal("AdOne", source.GetCurrent().BrandName);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void NoCache_StartsWithFallback()
    {
        using var source = new RemoteAdSource(new FakeAdsClient(), TempCachePath(), Fallback);
        Assert.Equal(Fallback, source.GetCurrent());
    }

    [Fact]
    public async Task CacheIsLoadedOnRestart_WhenFetchFails()
    {
        string path = TempCachePath();
        try
        {
            using (RemoteAdSource first = new(new FakeAdsClient(Result("AdOne", TimeSpan.FromSeconds(30))), path, Fallback))
            {
                await first.RefreshAsync(CancellationToken.None);
            }

            using RemoteAdSource second = new(new FakeAdsClient(), path, Fallback);
            Assert.Equal("AdOne", second.GetCurrent().BrandName);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class AdsStubHandler : HttpMessageHandler
    {
        public HttpRequestHeaders? LastHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastHeaders = request.Headers;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"brandName":"Acme","headline":"Buy now","tagline":"Limited offer","logoText":"AC","logoImageUrl":"https://cdn/a.png","websiteUrl":"https://acme.example","durationSeconds":45}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    [Fact]
    public async Task HttpAdsClient_ParsesAd_ClampsRotation_AndSendsBearerWhenProvided()
    {
        var handler = new AdsStubHandler();
        using var http = new HttpClient(handler);
        Func<CancellationToken, Task<string?>> tokenProvider = _ => Task.FromResult<string?>("the-token");
        var client = new HttpAdsApiClient(http, "http://127.0.0.1:9/", tokenProvider);

        AdFetchResult? result = await client.FetchCurrentAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Acme", result!.Ad.BrandName);
        Assert.Equal("Buy now", result.Ad.Headline);
        Assert.Equal("https://acme.example", result.Ad.WebsiteUrl);
        Assert.Equal("https://cdn/a.png", result.Ad.LogoImageUrl);
        Assert.Equal(TimeSpan.FromSeconds(45), result.Rotation);
        Assert.Equal("the-token", handler.LastHeaders!.Authorization?.Parameter);
    }

    [Fact]
    public async Task HttpAdsClient_NoToken_NoAuthorizationHeader()
    {
        var handler = new AdsStubHandler();
        using var http = new HttpClient(handler);
        Func<CancellationToken, Task<string?>> noToken = _ => Task.FromResult<string?>(null);
        var client = new HttpAdsApiClient(http, "http://127.0.0.1:9/", noToken);

        await client.FetchCurrentAsync(CancellationToken.None);

        Assert.Null(handler.LastHeaders!.Authorization);
    }
}
