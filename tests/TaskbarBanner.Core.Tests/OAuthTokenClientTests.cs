using System.Net;
using System.Text;
using TaskbarBanner.Core.Auth;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class OAuthTokenClientTests
{
    private static readonly OAuthOptions Options = new()
    {
        ClientId = "client-1",
        AuthorizationEndpoint = "https://idp.example/authorize",
        TokenEndpoint = "https://idp.example/token",
        RedirectUri = "http://127.0.0.1:9999/callback",
        RevocationEndpoint = "https://idp.example/revoke",
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> OnSend { get; set; } = _ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await OnSend(request);
        }
    }

    [Fact]
    public async Task ExchangeCode_SendsPkceParameters_AndParsesToken()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero) };
        var handler = new StubHandler
        {
            OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"acc","refresh_token":"ref","id_token":"idt","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json"),
            }),
        };
        using var http = new HttpClient(handler);
        var client = new OAuthTokenClient(Options, http, clock);

        OAuthToken token = await client.ExchangeCodeAsync("the-code", "the-verifier", Options.RedirectUri, CancellationToken.None);

        Assert.Equal("acc", token.AccessToken);
        Assert.Equal("ref", token.RefreshToken);
        Assert.Equal("idt", token.IdToken);
        Assert.Equal(clock.UtcNow.AddHours(1), token.ExpiresAtUtc);

        string body = handler.LastBody!;
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("client_id=client-1", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("code_verifier=the-verifier", body);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(Options.RedirectUri)}", body);
    }

    [Fact]
    public async Task Refresh_SendsRefreshGrant()
    {
        var handler = new StubHandler
        {
            OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"new-acc","refresh_token":"new-ref","expires_in":3600}""", Encoding.UTF8, "application/json"),
            }),
        };
        using var http = new HttpClient(handler);
        var client = new OAuthTokenClient(Options, http);

        OAuthToken token = await client.RefreshAsync("old-refresh", CancellationToken.None);

        Assert.Equal("new-acc", token.AccessToken);
        Assert.Equal("new-ref", token.RefreshToken);
        string body = handler.LastBody!;
        Assert.Contains("grant_type=refresh_token", body);
        Assert.Contains("refresh_token=old-refresh", body);
    }

    [Fact]
    public async Task InvalidGrant_ThrowsWithFlag()
    {
        var handler = new StubHandler
        {
            OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
            }),
        };
        using var http = new HttpClient(handler);
        var client = new OAuthTokenClient(Options, http);

        OAuthProtocolException ex = await Assert.ThrowsAsync<OAuthProtocolException>(
            () => client.RefreshAsync("old", CancellationToken.None));
        Assert.True(ex.IsInvalidGrant);
    }

    [Fact]
    public async Task ServerError_ThrowsWithoutInvalidGrantFlag()
    {
        var handler = new StubHandler
        {
            OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)),
        };
        using var http = new HttpClient(handler);
        var client = new OAuthTokenClient(Options, http);

        OAuthProtocolException ex = await Assert.ThrowsAsync<OAuthProtocolException>(
            () => client.ExchangeCodeAsync("code", "verifier", Options.RedirectUri, CancellationToken.None));
        Assert.False(ex.IsInvalidGrant);
    }
}
