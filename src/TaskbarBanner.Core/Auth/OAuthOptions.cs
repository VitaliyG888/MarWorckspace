namespace TaskbarBanner.Core.Auth;

public sealed record OAuthOptions
{
    public required string ClientId { get; init; }
    public required string AuthorizationEndpoint { get; init; }
    public required string TokenEndpoint { get; init; }
    public string RedirectUri { get; init; } = string.Empty;
    public string RevocationEndpoint { get; init; } = string.Empty;
    public string Scope { get; init; } = "openid profile offline_access";
}
