using System.Net.Http;
using System.Text;
using TaskbarBanner.Core.Reporting;

namespace TaskbarBanner.App.Reporting;

public sealed class HttpReportApiClient : IReportApiClient
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly Func<string?>? _deviceIdProvider;
    private readonly Func<string?>? _signingSecretProvider;

    public HttpReportApiClient(
        HttpClient http,
        string baseUrl,
        Func<string?>? deviceIdProvider = null,
        Func<string?>? signingSecretProvider = null)
    {
        _http = http;
        _endpoint = new Uri(new Uri(baseUrl), "api/v1/minutes");
        _deviceIdProvider = deviceIdProvider;
        _signingSecretProvider = signingSecretProvider;
    }

    public async Task<bool> TryUploadAsync(IReadOnlyList<VerifiedMinute> batch, CancellationToken cancellationToken)
    {
        string json = ReportPayloadJson.Serialize(ReportPayloadFactory.Build(batch));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        string? deviceId = _deviceIdProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            request.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);
        }

        string? secret = _signingSecretProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            long unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string signature = RequestSigner.Sign(
                secret,
                "POST",
                _endpoint.AbsolutePath,
                json,
                unixSeconds);
            request.Headers.TryAddWithoutValidation("X-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-Signature", signature);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
