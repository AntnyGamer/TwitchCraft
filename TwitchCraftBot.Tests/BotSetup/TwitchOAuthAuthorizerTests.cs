using System.Text.Json;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.BotSetup;

public sealed class TwitchOAuthAuthorizerTests
{
    [Fact]
    public void DefaultAuthorizationResultIsNotSuccessful()
    {
        Assert.False(default(TwitchOAuthResult).IsSuccess);
    }

    [Fact]
    public void AcceptsMatchingTokenWithEveryRequiredScope()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "client_id": "client123",
              "login": "BotAccount",
              "user_id": "123456",
              "expires_in": 3600,
              "scopes": [
                "chat:read",
                "chat:edit",
                "moderator:read:chatters",
                "moderator:read:followers"
              ]
            }
            """);

        Assert.True(TwitchOAuthAuthorizer.TryReadValidatedIdentity(
            document.RootElement,
            "client123",
            out string login,
            out string error));
        Assert.Equal("botaccount", login);
        Assert.Empty(error);
    }

    [Fact]
    public void RejectsTokenWithoutFollowerPermission()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "client_id": "client123",
              "login": "botaccount",
              "user_id": "123456",
              "expires_in": 3600,
              "scopes": ["chat:read", "chat:edit", "moderator:read:chatters"]
            }
            """);

        Assert.False(TwitchOAuthAuthorizer.TryReadValidatedIdentity(
            document.RootElement,
            "client123",
            out string login,
            out string error));
        Assert.Empty(login);
        Assert.Contains("moderator:read:followers", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsTwitchDeviceAuthorizationWithoutALocalhostRedirect()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "device_code": "device-secret",
              "user_code": "ABCD-EFGH",
              "verification_uri": "https://www.twitch.tv/activate",
              "expires_in": 1800,
              "interval": 5
            }
            """);

        Assert.True(TwitchOAuthAuthorizer.TryReadDeviceAuthorization(
            document.RootElement,
            out TwitchDeviceAuthorization authorization,
            out string error));
        Assert.Equal("device-secret", authorization.DeviceCode);
        Assert.Equal("ABCD-EFGH", authorization.UserCode);
        Assert.Equal(1800, authorization.ExpiresInSeconds);
        Assert.Equal(5, authorization.IntervalSeconds);
        Assert.Equal(Uri.UriSchemeHttps, authorization.VerificationUri.Scheme);
        Assert.Equal("www.twitch.tv", authorization.VerificationUri.Host);
        Assert.Contains("public=true", authorization.VerificationUri.Query, StringComparison.Ordinal);
        Assert.Contains("device-code=ABCD-EFGH", authorization.VerificationUri.Query, StringComparison.Ordinal);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://example.com/activate")]
    public void RejectsUntrustedDeviceAuthorizationPages(string verificationUri)
    {
        using JsonDocument document = JsonDocument.Parse($$"""
            {
              "device_code": "device-secret",
              "user_code": "ABCD-EFGH",
              "verification_uri": "{{verificationUri}}",
              "expires_in": 1800,
              "interval": 5
            }
            """);

        Assert.False(TwitchOAuthAuthorizer.TryReadDeviceAuthorization(
            document.RootElement,
            out _,
            out string error));
        Assert.Contains("invalid authorization page", error, StringComparison.OrdinalIgnoreCase);
    }
}
