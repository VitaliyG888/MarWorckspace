using TaskbarBanner.Core.Auth;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class DpapiTokenStoreTests
{
    [Fact]
    public void SaveAndRead_RoundTripsToken()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tbb-auth-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DpapiTokenStore(file);
            var original = new OAuthToken("access-token", "refresh-token", "id-token",
                new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.Zero));

            store.Save(original);
            Assert.True(store.TryRead(out OAuthToken read));
            Assert.Equal(original.AccessToken, read.AccessToken);
            Assert.Equal(original.RefreshToken, read.RefreshToken);
            Assert.Equal(original.IdToken, read.IdToken);
            Assert.Equal(original.ExpiresAtUtc, read.ExpiresAtUtc);
            Assert.Equal(original.ObtainedAtUtc, read.ObtainedAtUtc);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void MissingFile_ReturnsFalse()
    {
        var store = new DpapiTokenStore(Path.Combine(Path.GetTempPath(), $"tbb-missing-{Guid.NewGuid():N}.json"));
        Assert.False(store.TryRead(out _));
    }

    [Fact]
    public void Delete_RemovesToken()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tbb-auth-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DpapiTokenStore(file);
            store.Save(new OAuthToken("a", "r", "i", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow));

            store.Delete();
            Assert.False(File.Exists(file));
            Assert.False(store.TryRead(out _));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
