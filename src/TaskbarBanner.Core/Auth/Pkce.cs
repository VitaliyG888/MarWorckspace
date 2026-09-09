using System.Security.Cryptography;
using System.Text;

namespace TaskbarBanner.Core.Auth;

public static class Pkce
{
    public static string GenerateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return ToBase64Url(bytes);
    }

    public static string ComputeCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return ToBase64Url(hash);
    }

    public static string GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return ToBase64Url(bytes);
    }

    public static string ToBase64Url(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
