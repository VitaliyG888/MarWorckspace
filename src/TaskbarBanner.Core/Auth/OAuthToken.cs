namespace TaskbarBanner.Core.Auth;

public sealed record OAuthToken(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset ObtainedAtUtc);
