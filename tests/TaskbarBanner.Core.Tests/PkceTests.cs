using System.Security.Cryptography;
using System.Text;
using TaskbarBanner.Core.Auth;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class PkceTests
{
    [Fact]
    public void CodeVerifier_IsUrlSafe_AndReasonableLength()
    {
        string verifier = Pkce.GenerateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches(@"^[A-Za-z0-9\-_]+$", verifier);
        Assert.DoesNotContain("=", verifier);
    }

    [Fact]
    public void Challenge_MatchesManualSha256Computation()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        string expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expected, Pkce.ComputeCodeChallenge(verifier));
    }

    [Fact]
    public void DifferentVerifiers_ProduceDifferentChallenges()
    {
        string a = Pkce.GenerateCodeVerifier();
        string b = Pkce.GenerateCodeVerifier();

        Assert.NotEqual(a, b);
        Assert.NotEqual(Pkce.ComputeCodeChallenge(a), Pkce.ComputeCodeChallenge(b));
    }
}
