namespace TaskbarBanner.Core.Auth;

public sealed class AuthStateChangedEventArgs : EventArgs
{
    public required bool IsAuthenticated { get; init; }
    public required string Description { get; init; }
}
