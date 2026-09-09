using System.Net.Sockets;
using System.Text;

namespace TaskbarBanner.Core.Auth;

public sealed class AuthSessionManager
{
    public static readonly TimeSpan DefaultRefreshLead = TimeSpan.FromSeconds(60);

    private readonly OAuthOptions _options;
    private readonly ITokenStore _store;
    private readonly IAuthTokenClient _client;
    private readonly IClock _clock;
    private readonly TimeSpan _refreshLead;

    private OAuthToken? _currentToken;
    private PendingLogin? _pending;

    public AuthSessionManager(
        OAuthOptions options,
        ITokenStore store,
        IAuthTokenClient client,
        IClock? clock = null,
        TimeSpan? refreshLead = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? new SystemClock();
        _refreshLead = refreshLead ?? DefaultRefreshLead;
    }

    public event EventHandler<AuthStateChangedEventArgs>? StateChanged;

    public bool IsAuthenticated { get; private set; }

    public string Description { get; private set; } = "Not signed in";

    public bool IsLoginInProgress => _pending is not null;

    public string? AccessToken => _currentToken?.AccessToken;

    public string? CurrentRedirectUri => _pending?.RedirectUri;

    public bool NeedsRefresh
        => _currentToken is { RefreshToken: not null } token
           && _clock.UtcNow + _refreshLead >= token.ExpiresAtUtc;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!_store.TryRead(out OAuthToken stored))
        {
            SetState(false, "Not signed in");
            return;
        }

        _currentToken = stored;
        DateTimeOffset now = _clock.UtcNow;

        if (now + _refreshLead < stored.ExpiresAtUtc)
        {
            SetState(true, "Session restored");
            return;
        }

        if (string.IsNullOrWhiteSpace(stored.RefreshToken))
        {
            _store.Delete();
            _currentToken = null;
            SetState(false, "Session expired - sign in again");
            return;
        }

        await TryRefreshCoreAsync(stored.RefreshToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Uri> StartLoginAsync(CancellationToken cancellationToken)
    {
        if (_pending is not null)
        {
            throw new InvalidOperationException("A login flow is already in progress.");
        }

        string verifier = Pkce.GenerateCodeVerifier();
        string state = Pkce.GenerateNonce();
        string nonce = Pkce.GenerateNonce();
        string redirectUri = ResolveRedirectUri();

        string challenge = Pkce.ComputeCodeChallenge(verifier);
        _pending = new PendingLogin(state, verifier, redirectUri, nonce);
        SetState(false, "Waiting for sign in...");

        var query = new StringBuilder();
        query.Append("response_type=code");
        query.Append('&').Append("client_id=").Append(Uri.EscapeDataString(_options.ClientId));
        query.Append('&').Append("redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        query.Append('&').Append("scope=").Append(Uri.EscapeDataString(_options.Scope));
        query.Append('&').Append("state=").Append(Uri.EscapeDataString(state));
        query.Append('&').Append("nonce=").Append(Uri.EscapeDataString(nonce));
        query.Append('&').Append("code_challenge=").Append(Uri.EscapeDataString(challenge));
        query.Append('&').Append("code_challenge_method=S256");

        string separator = _options.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        return new Uri(_options.AuthorizationEndpoint + separator + query);
    }

    public async Task CompleteLoginAsync(string callbackUrl, CancellationToken cancellationToken)
    {
        if (_pending is null)
        {
            throw new InvalidOperationException("No login flow is in progress.");
        }

        PendingLogin pending = _pending;
        IReadOnlyDictionary<string, string> query = ParseQuery(new Uri(callbackUrl).Query);

        if (!query.TryGetValue("state", out string? returnedState) || returnedState != pending.State)
        {
            _pending = null;
            SetState(false, "Sign in failed: state mismatch");
            throw new OAuthProtocolException("OAuth callback state mismatch.");
        }

        if (query.TryGetValue("error", out string? error))
        {
            _pending = null;
            string detail = query.TryGetValue("error_description", out string? d) ? d : string.Empty;
            SetState(false, "Sign in failed");
            throw new OAuthProtocolException($"Authorization error: {error} {detail}".Trim());
        }

        if (!query.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code))
        {
            _pending = null;
            SetState(false, "Sign in failed: no code");
            throw new OAuthProtocolException("Authorization callback did not contain a code.");
        }

        _pending = null;
        OAuthToken token = await _client
            .ExchangeCodeAsync(code, pending.CodeVerifier, pending.RedirectUri, cancellationToken)
            .ConfigureAwait(false);

        SaveToken(token);
        SetState(true, "Signed in");
    }

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        if (_currentToken?.RefreshToken is not { } refreshToken)
        {
            return false;
        }

        return await TryRefreshCoreAsync(refreshToken, cancellationToken).ConfigureAwait(false);
    }

    public void CancelLogin()
    {
        if (_pending is null)
        {
            return;
        }

        _pending = null;
        SetState(false, "Sign in cancelled");
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (_currentToken?.RefreshToken is { } refreshToken)
        {
            try
            {
                await _client.RevokeAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // revocation is best-effort
            }
        }

        _store.Delete();
        _currentToken = null;
        _pending = null;
        SetState(false, "Signed out");
    }

    private async Task<bool> TryRefreshCoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            OAuthToken refreshed = await _client.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            SaveToken(refreshed);
            SetState(true, "Session refreshed");
            return true;
        }
        catch (OAuthProtocolException ex) when (ex.IsInvalidGrant)
        {
            _store.Delete();
            _currentToken = null;
            SetState(false, "Session expired - sign in again");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or OAuthProtocolException)
        {
            SetState(false, "Offline - token refresh pending");
            return false;
        }
    }

    private void SaveToken(OAuthToken token)
    {
        _currentToken = token;
        _store.Save(token);
    }

    private string ResolveRedirectUri()
    {
        if (!string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            return _options.RedirectUri;
        }

        return $"http://127.0.0.1:{FindFreeTcpPort()}/callback";
    }

    private static int FindFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (string part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int index = part.IndexOf('=');
            if (index < 0)
            {
                result[Uri.UnescapeDataString(part)] = string.Empty;
            }
            else
            {
                result[Uri.UnescapeDataString(part[..index])] = Uri.UnescapeDataString(part[(index + 1)..]);
            }
        }

        return result;
    }

    private void SetState(bool authenticated, string description)
    {
        IsAuthenticated = authenticated;
        Description = description;
        StateChanged?.Invoke(this, new AuthStateChangedEventArgs
        {
            IsAuthenticated = authenticated,
            Description = description,
        });
    }

    private sealed record PendingLogin(string State, string CodeVerifier, string RedirectUri, string Nonce);
}
