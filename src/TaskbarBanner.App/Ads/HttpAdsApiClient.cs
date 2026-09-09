using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarBanner.App.Ads;

public sealed record AdDto(
    string? BrandName,
    string? Headline,
    string? Tagline,
    string? LogoText,
    string? LogoImageUrl,
    string? WebsiteUrl,
    int? DurationSeconds);

public interface IAdsApiClient
{
    Task<AdFetchResult?> FetchCurrentAsync(CancellationToken cancellationToken);
}

public sealed class HttpAdsApiClient : IAdsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly Func<CancellationToken, Task<string?>> _accessTokenProvider;

    public HttpAdsApiClient(
        HttpClient http,
        string baseUrl,
        Func<CancellationToken, Task<string?>> accessTokenProvider)
    {
        _http = http;
        _endpoint = new Uri(new Uri(baseUrl), "api/v1/ads/current");
        _accessTokenProvider = accessTokenProvider;
    }

    public async Task<AdFetchResult?> FetchCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            string? accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AdDto? dto = JsonSerializer.Deserialize<AdDto>(json, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Headline) || string.IsNullOrWhiteSpace(dto.Tagline))
            {
                return null;
            }

            var ad = new AdContent(
                string.IsNullOrWhiteSpace(dto.BrandName) ? "Ad" : dto.BrandName,
                dto.Headline,
                dto.Tagline,
                string.IsNullOrWhiteSpace(dto.LogoText) ? "AD" : dto.LogoText,
                string.IsNullOrWhiteSpace(dto.WebsiteUrl) ? null : dto.WebsiteUrl,
                string.IsNullOrWhiteSpace(dto.LogoImageUrl) ? null : dto.LogoImageUrl);

            int seconds = Math.Clamp(dto.DurationSeconds ?? 30, 10, 300);
            return new AdFetchResult(ad, TimeSpan.FromSeconds(seconds));
        }
        catch
        {
            return null;
        }
    }
}
