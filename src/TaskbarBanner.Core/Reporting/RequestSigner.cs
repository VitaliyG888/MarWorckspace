using System.Security.Cryptography;
using System.Text;

namespace TaskbarBanner.Core.Reporting;

public static class RequestSigner
{
    public static string Sign(string secret, string method, string path, string body, long unixSeconds)
    {
        string bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        string canonical = $"{unixSeconds}\n{method.ToUpperInvariant()}\n{path}\n{bodyHash}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
