using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskbarBanner.Core.Auth;

public sealed class DpapiTokenStore : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("taskbanner.auth.v1");

    private readonly string _filePath;

    public DpapiTokenStore(string filePath)
    {
        _filePath = filePath;
    }

    public bool TryRead(out OAuthToken token)
    {
        token = default!;
        if (!File.Exists(_filePath))
        {
            return false;
        }

        try
        {
            byte[] encrypted = File.ReadAllBytes(_filePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(plain);
            var dto = JsonSerializer.Deserialize<TokenDto>(json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.AccessToken))
            {
                return false;
            }

            token = new OAuthToken(
                dto.AccessToken,
                string.IsNullOrWhiteSpace(dto.RefreshToken) ? null : dto.RefreshToken,
                string.IsNullOrWhiteSpace(dto.IdToken) ? null : dto.IdToken,
                dto.ExpiresAtUtc,
                dto.ObtainedAtUtc);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Save(OAuthToken token)
    {
        string json = JsonSerializer.Serialize(new TokenDto
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            IdToken = token.IdToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            ObtainedAtUtc = token.ObtainedAtUtc,
        });

        string directory = Path.GetDirectoryName(_filePath)!;
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encrypted);
    }

    public void Delete()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private sealed class TokenDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public DateTimeOffset ObtainedAtUtc { get; set; }
    }
}
