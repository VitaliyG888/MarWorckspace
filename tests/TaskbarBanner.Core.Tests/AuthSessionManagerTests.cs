using TaskbarBanner.Core.Auth;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class AuthSessionManagerTests
{
    private static readonly OAuthOptions Options = new()
    {
        ClientId = "client-1",
        AuthorizationEndpoint = "https://idp.example/authorize",
        TokenEndpoint = "https://idp.example/token",
        RedirectUri = "http://127.0.0.1:9999/callback",
        RevocationEndpoint = "https://idp.example/revoke",
    };

    private static AuthSessionManager CreateManager(FakeAuthTokenClient client, MemoryTokenStore store, FakeClock clock)
        => new(Options, store, client, clock);

    private static OAuthToken MakeToken(FakeClock clock, TimeSpan expiresIn)
        => new("access-token", "refresh-token", "id-token", clock.UtcNow + expiresIn, clock.UtcNow);

    private static string ExtractQueryValue(string url, string name)
    {
        string query = new Uri(url).Query.TrimStart('?');
        foreach (string part in query.Split('&'))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name)
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        Assert.Fail($"Query parameter {name} not found in {url}");
        return string.Empty;
    }

    [Fact]
    public async Task StartLogin_BuildsAuthorizeUri_WithPkce_WithoutLeakingVerifier()
    {
        var client = new FakeAuthTokenClient();
        var store = new MemoryTokenStore();
        var clock = new FakeClock();
        AuthSessionManager manager = CreateManager(client, store, clock);

        Uri uri = await manager.StartLoginAsync(CancellationToken.None);
        string url = uri.ToString();

        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=client-1", url);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(Options.RedirectUri)}", url);
        Assert.Contains("scope=", url);
        Assert.Contains("code_challenge=", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=", url);
        Assert.DoesNotContain("code_verifier", url);
        Assert.True(manager.IsLoginInProgress);
    }

    [Fact]
    public async Task CompleteLogin_ExchangesCodeWithVerifier_AndAuthenticates()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient
        {
            TokenToReturn = MakeToken(clock, TimeSpan.FromHours(2)),
        };
        var store = new MemoryTokenStore();
        AuthSessionManager manager = CreateManager(client, store, clock);
        var states = new List<AuthStateChangedEventArgs>();
        manager.StateChanged += (_, e) => states.Add(e);

        Uri uri = await manager.StartLoginAsync(CancellationToken.None);
        string state = ExtractQueryValue(uri.ToString(), "state");
        string callback = $"{Options.RedirectUri}?code=authcode&state={Uri.EscapeDataString(state)}";

        await manager.CompleteLoginAsync(callback, CancellationToken.None);

        Assert.True(manager.IsAuthenticated);
        Assert.Equal("authcode", client.LastCode);
        Assert.Equal(Options.RedirectUri, client.LastRedirectUri);
        Assert.NotNull(client.LastVerifier);
        Assert.Equal(ExtractQueryValue(uri.ToString(), "code_challenge"), Pkce.ComputeCodeChallenge(client.LastVerifier!));
        Assert.NotNull(store.Token);
        Assert.Equal("access-token", store.Token!.AccessToken);
        Assert.Contains(states, s => s.IsAuthenticated);
    }

    [Fact]
    public async Task CompleteLogin_StateMismatch_Throws_WithoutExchange()
    {
        var client = new FakeAuthTokenClient();
        var store = new MemoryTokenStore();
        AuthSessionManager manager = CreateManager(client, store, new FakeClock());

        await manager.StartLoginAsync(CancellationToken.None);
        string callback = $"{Options.RedirectUri}?code=authcode&state=wrong-state";

        await Assert.ThrowsAsync<OAuthProtocolException>(
            () => manager.CompleteLoginAsync(callback, CancellationToken.None));

        Assert.Equal(0, client.ExchangeCalls);
        Assert.False(manager.IsAuthenticated);
    }

    [Fact]
    public async Task CancelLogin_ClearsPendingFlow()
    {
        var manager = CreateManager(new FakeAuthTokenClient(), new MemoryTokenStore(), new FakeClock());
        await manager.StartLoginAsync(CancellationToken.None);
        Assert.True(manager.IsLoginInProgress);

        manager.CancelLogin();

        Assert.False(manager.IsLoginInProgress);
        Assert.False(manager.IsAuthenticated);
    }

    [Fact]
    public async Task Load_UnexpiredToken_Authenticates_WithoutRefresh()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient();
        var store = new MemoryTokenStore { Token = MakeToken(clock, TimeSpan.FromHours(2)) };
        AuthSessionManager manager = CreateManager(client, store, clock);

        await manager.LoadAsync(CancellationToken.None);

        Assert.True(manager.IsAuthenticated);
        Assert.Equal(0, client.RefreshCalls);
    }

    [Fact]
    public async Task Load_ExpiredToken_Refreshes_AndPersistsNewToken()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient
        {
            TokenToReturn = MakeToken(clock, TimeSpan.FromHours(2)),
        };
        var store = new MemoryTokenStore { Token = MakeToken(clock, TimeSpan.FromMinutes(-10)) };
        AuthSessionManager manager = CreateManager(client, store, clock);

        await manager.LoadAsync(CancellationToken.None);

        Assert.True(manager.IsAuthenticated);
        Assert.Equal(1, client.RefreshCalls);
        Assert.Equal("refresh-token", client.LastRefreshToken);
        Assert.Equal("access-token", store.Token!.AccessToken);
    }

    [Fact]
    public async Task Load_InvalidGrant_ClearsToken_AndNotAuthenticated()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient { ThrowInvalidGrant = true };
        var store = new MemoryTokenStore { Token = MakeToken(clock, TimeSpan.FromMinutes(-10)) };
        AuthSessionManager manager = CreateManager(client, store, clock);

        await manager.LoadAsync(CancellationToken.None);

        Assert.False(manager.IsAuthenticated);
        Assert.Null(store.Token);
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task Load_TransientNetworkError_KeepsToken_ButGatesOff()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient { ThrowTransient = true };
        var store = new MemoryTokenStore { Token = MakeToken(clock, TimeSpan.FromMinutes(-10)) };
        AuthSessionManager manager = CreateManager(client, store, clock);

        await manager.LoadAsync(CancellationToken.None);

        Assert.False(manager.IsAuthenticated);
        Assert.NotNull(store.Token);
        Assert.Contains("Offline", manager.Description);
    }

    [Fact]
    public async Task NeedsRefresh_TurnsOnNearExpiry()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient();
        var store = new MemoryTokenStore { Token = MakeToken(clock, TimeSpan.FromHours(1)) };
        AuthSessionManager manager = CreateManager(client, store, clock);

        await manager.LoadAsync(CancellationToken.None);
        Assert.True(manager.IsAuthenticated);
        Assert.False(manager.NeedsRefresh);

        clock.UtcNow += TimeSpan.FromMinutes(59);
        Assert.True(manager.NeedsRefresh);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_ClearsStore_AndDeauthenticates()
    {
        var clock = new FakeClock();
        var client = new FakeAuthTokenClient();
        var store = new MemoryTokenStore { Token = MakeToken(clock, TimeSpan.FromHours(1)) };
        AuthSessionManager manager = CreateManager(client, store, clock);
        await manager.LoadAsync(CancellationToken.None);
        Assert.True(manager.IsAuthenticated);

        await manager.LogoutAsync(CancellationToken.None);

        Assert.Equal(1, client.RevokeCalls);
        Assert.Equal("refresh-token", client.LastRefreshToken);
        Assert.False(manager.IsAuthenticated);
        Assert.Null(store.Token);
    }
}
