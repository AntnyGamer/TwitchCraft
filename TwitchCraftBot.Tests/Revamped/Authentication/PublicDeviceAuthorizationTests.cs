using System.Text.Json;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Authentication;

public sealed class PublicDeviceAuthorizationTests
{
    [Fact]
    public void ClassifiesRefreshFailuresThatNeedUserAuthorization()
    {
        Assert.False(default(TwitchOAuthResult).IsSuccess);
        Assert.True(TwitchOAuthAuthorizer.TwitchCraftOAuthConfigured);
        Assert.NotEmpty(TwitchOAuthAuthorizer.TwitchCraftClientId);
        Assert.True(TwitchOAuthAuthorizer.ShouldUseDeviceAuth("invalid refresh token"));
        Assert.True(TwitchOAuthAuthorizer.ShouldUseDeviceAuth("revoked refresh token"));
        Assert.False(TwitchOAuthAuthorizer.ShouldUseDeviceAuth("Twitch is temporarily unavailable"));
        Assert.False(TwitchOAuthAuthorizer.ShouldUseDeviceAuth("missing client secret"));
        Assert.True(TwitchOAuthAuthorizer.IsClientSecretFailure("missing client secret"));
        Assert.False(TwitchOAuthAuthorizer.IsClientSecretFailure("invalid refresh token"));
        Assert.DoesNotContain(
            typeof(TwitchConfig).GetProperties(),
            property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
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

        Assert.True(TwitchOAuthAuthorizer.TryReadIdentity(
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

        Assert.False(TwitchOAuthAuthorizer.TryReadIdentity(
            document.RootElement,
            "client123",
            out string login,
            out string error));
        Assert.Empty(login);
        Assert.Contains("moderator:read:followers", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"client_id":"other","login":"botaccount","user_id":"42","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", "different Twitch Client ID")]
    [InlineData("""{"client_id":"client123","login":"","user_id":"42","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", "valid user account")]
    [InlineData("""{"client_id":"client123","login":"botaccount","user_id":"","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", "valid user account")]
    [InlineData("""{"client_id":"client123","login":"botaccount","user_id":"42","expires_in":0,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", "expired or invalid")]
    [InlineData("""{"client_id":"client123","login":"botaccount","user_id":"42","expires_in":3600}""", "token permissions")]
    public void RejectsInvalidOrStaleTokenValidationResponses(string json, string expectedError)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        bool accepted = TwitchOAuthAuthorizer.TryReadIdentity(
            document.RootElement,
            "client123",
            out _,
            out string error);

        Assert.False(accepted);
        Assert.Contains(expectedError, error, StringComparison.OrdinalIgnoreCase);
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

        Assert.True(TwitchOAuthAuthorizer.TryReadDeviceAuth(
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

        Assert.False(TwitchOAuthAuthorizer.TryReadDeviceAuth(
            document.RootElement,
            out _,
            out string error));
        Assert.Contains("invalid authorization page", error, StringComparison.OrdinalIgnoreCase);
    }
}
