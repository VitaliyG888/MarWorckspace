namespace TaskbarBanner.Core.Auth;

public interface ITokenStore
{
    bool TryRead(out OAuthToken token);

    void Save(OAuthToken token);

    void Delete();
}
