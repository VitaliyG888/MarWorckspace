using TaskbarBanner.Core.Auth;

namespace TaskbarBanner.Core.Tests;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class MemoryTokenStore : ITokenStore
{
    public OAuthToken? Token { get; set; }
    public int SaveCount { get; private set; }
    public int DeleteCount { get; private set; }

    public bool TryRead(out OAuthToken token)
    {
        if (Token is null)
        {
            token = default!;
            return false;
        }

        token = Token;
        return true;
    }

    public void Save(OAuthToken token)
    {
        SaveCount++;
        Token = token;
    }

    public void Delete()
    {
        DeleteCount++;
        Token = null;
    }
}

internal sealed class FakeAuthTokenClient : IAuthTokenClient
{
    public OAuthToken? TokenToReturn { get; set; }
    public bool ThrowInvalidGrant { get; set; }
    public bool ThrowTransient { get; set; }

    public int ExchangeCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public int RevokeCalls { get; private set; }
    public string? LastCode { get; private set; }
    public string? LastVerifier { get; private set; }
    public string? LastRedirectUri { get; private set; }
    public string? LastRefreshToken { get; private set; }

    public Task<OAuthToken> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        ExchangeCalls++;
        LastCode = code;
        LastVerifier = codeVerifier;
        LastRedirectUri = redirectUri;
        return Task.FromResult(NextToken());
    }

    public Task<OAuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RefreshCalls++;
        LastRefreshToken = refreshToken;
        if (ThrowInvalidGrant)
        {
            throw new OAuthProtocolException("invalid_grant", isInvalidGrant: true);
        }

        if (ThrowTransient)
        {
            throw new HttpRequestException("offline");
        }

        return Task.FromResult(NextToken());
    }

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RevokeCalls++;
        LastRefreshToken = refreshToken;
        return Task.CompletedTask;
    }

    private OAuthToken NextToken() => TokenToReturn ?? new OAuthToken("access", "refresh", "id", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
}
