using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TaskbarBanner.App.Reporting;
using TaskbarBanner.Core.Reporting;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class RequestSignerTests
{
    [Fact]
    public void Signature_IsDeterministic()
    {
        string a = RequestSigner.Sign("secret", "POST", "/api/v1/minutes", "{\"x\":1}", 1700000000);
        string b = RequestSigner.Sign("secret", "POST", "/api/v1/minutes", "{\"x\":1}", 1700000000);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Signature_ChangesWithBody_Time_OrSecret()
    {
        string baseSignature = RequestSigner.Sign("secret", "POST", "/api/v1/minutes", "body", 1700000000);

        Assert.NotEqual(baseSignature, RequestSigner.Sign("secret", "POST", "/api/v1/minutes", "other-body", 1700000000));
        Assert.NotEqual(baseSignature, RequestSigner.Sign("secret", "POST", "/api/v1/minutes", "body", 1700000001));
        Assert.NotEqual(baseSignature, RequestSigner.Sign("other-secret", "POST", "/api/v1/minutes", "body", 1700000000));
    }
}

public class HttpReportApiClientSigningTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestHeaders? Headers { get; private set; }
        public string? Body { get; private set; }
        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Headers = request.Headers;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Upload_WithSecret_AddsDeviceIdTimestampAndValidSignatureHeaders()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var client = new HttpReportApiClient(
            http,
            "http://127.0.0.1:9/",
            () => "device-123",
            () => "top-secret");

        var minute = new VerifiedMinute
        {
            IdempotencyKey = "k",
            SessionId = "s",
            DeviceId = "device-123",
            MinuteStartUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            MinuteEndUtc = new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero),
        };

        bool ok = await client.TryUploadAsync(new[] { minute }, CancellationToken.None);

        Assert.True(ok);
        Assert.True(handler.Headers!.TryGetValues("X-Device-Id", out var deviceIds));
        Assert.Equal("device-123", Assert.Single(deviceIds));
        Assert.True(handler.Headers.TryGetValues("X-Timestamp", out var timestamps));
        long ts = long.Parse(Assert.Single(timestamps), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(handler.Headers.TryGetValues("X-Signature", out var signatures));
        string signature = Assert.Single(signatures);

        string expected = RequestSigner.Sign("top-secret", "POST", handler.Path!, handler.Body!, ts);
        Assert.Equal(expected, signature);
    }

    [Fact]
    public async Task Upload_WithoutSecret_OmitsSignatureAndDeviceHeaders()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var client = new HttpReportApiClient(http, "http://127.0.0.1:9/");

        await client.TryUploadAsync(Array.Empty<VerifiedMinute>(), CancellationToken.None);

        Assert.False(handler.Headers!.Contains("X-Device-Id"));
        Assert.False(handler.Headers.Contains("X-Signature"));
        Assert.False(handler.Headers.Contains("X-Timestamp"));
    }
}
