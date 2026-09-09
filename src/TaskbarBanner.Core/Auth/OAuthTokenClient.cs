using System.Text.Json;

namespace TaskbarBanner.Core.Auth;

public sealed class OAuthProtocolException : Exception
{
    public OAuthProtocolException(string message, bool isInvalidGrant = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IsInvalidGrant = isInvalidGrant;
    }

    public bool IsInvalidGrant { get; }
}

public sealed class OAuthTokenClient : IAuthTokenClient
{
    private readonly OAuthOptions _options;
    private readonly HttpClient _http;
    private readonly IClock _clock;

    public OAuthTokenClient(OAuthOptions options, HttpClient http, IClock? clock = null)
    {
        _options = options;
        _http = http;
        _clock = clock ?? new SystemClock();
    }

    public async Task<OAuthToken> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        });

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OAuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["refresh_token"] = refreshToken,
        });

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.RevocationEndpoint))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevocationEndpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
        });

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OAuthToken> ReadTokenResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            bool invalidGrant = body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);
            throw new OAuthProtocolException(
                $"Token endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(body)}",
                isInvalidGrant: invalidGrant);
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        string? accessToken = root.TryGetProperty("access_token", out JsonElement access) ? access.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new OAuthProtocolException("Token response did not contain access_token.");
        }

        string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement refresh) ? refresh.GetString() : null;
        string? idToken = root.TryGetProperty("id_token", out JsonElement id) ? id.GetString() : null;
        long expiresIn = root.TryGetProperty("expires_in", out JsonElement expires) && expires.ValueKind == JsonValueKind.Number
            ? expires.GetInt64()
            : 3600;

        DateTimeOffset now = _clock.UtcNow;
        return new OAuthToken(accessToken, refreshToken, idToken, now.AddSeconds(expiresIn), now);
    }

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400];
}
