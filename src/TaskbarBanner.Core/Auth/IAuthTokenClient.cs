namespace TaskbarBanner.Core.Auth;

public interface IAuthTokenClient
{
    Task<OAuthToken> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken);

    Task<OAuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}
