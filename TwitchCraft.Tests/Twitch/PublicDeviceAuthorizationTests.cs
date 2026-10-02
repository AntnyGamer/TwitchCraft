using System;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

public sealed class PublicDeviceAuthorizationTests
{
    [Fact]
    public void PublicClientConfigurationDoesNotPersistAClientSecret()
    {
        Assert.False(default(TwitchOAuthResult).IsSuccess);
        Assert.True(TwitchOAuthAuthorizer.IsOAuthConfigured);
        Assert.NotEmpty(TwitchOAuthAuthorizer.ApplicationClientID);
        JObject persistedConfig = JObject.FromObject(new TwitchConfig());
        Assert.DoesNotContain(
            persistedConfig.Properties(),
            property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("invalid refresh token", true)]
    [InlineData("revoked refresh token", true)]
    [InlineData("expired refresh token", true)]
    [InlineData("REFRESH TOKEN EXPIRED", true)]
    [InlineData("Twitch is temporarily unavailable", false)]
    [InlineData("missing client secret", false)]
    [InlineData("", false)]
    public void ShouldUseDeviceAuth_OnlyRequestsAuthorizationForUnusableRefreshTokens(string error, bool expected)
    {
        Assert.Equal(expected, TwitchOAuthAuthorizer.ShouldUseDeviceAuth(error));
    }

    [Theory]
    [InlineData("missing client secret", true)]
    [InlineData("CLIENT_SECRET is required", true)]
    [InlineData("invalid client secret", true)]
    [InlineData("rejected client secret", true)]
    [InlineData("invalid refresh token", false)]
    [InlineData("client secret configuration", false)]
    [InlineData("", false)]
    public void IsClientSecretFailure_RequiresBothASecretAndFailureReason(string error, bool expected)
    {
        Assert.Equal(expected, TwitchOAuthAuthorizer.IsClientSecretFailure(error));
    }

    [Theory]
    [InlineData("chat:read")]
    [InlineData("chat:edit")]
    [InlineData("moderator:read:chatters")]
    [InlineData("moderator:read:followers")]
    public void RejectsTokenWhenAnyRequiredPermissionIsMissing(string missingScope)
    {
        string[] scopes = ["chat:read", "chat:edit", "moderator:read:chatters", "moderator:read:followers"];
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            client_id = "client123",
            login = "botaccount",
            user_id = "123456",
            expires_in = 3600,
            scopes = Array.FindAll(scopes, scope => scope != missingScope)
        }));

        Assert.False(TwitchOAuthAuthorizer.TryReadIdentity(
            document.RootElement,
            "client123",
            out string login,
            out string error));
        Assert.Empty(login);
        Assert.Contains(missingScope, error, StringComparison.Ordinal);
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
    public void AcceptsTwitchDeviceAuthorizationWithoutLocalhostRedirect()
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
    [InlineData("http://www.twitch.tv/activate")]
    [InlineData("https://twitch.tv.example.com/activate")]
    [InlineData("https://eviltwitch.tv/activate")]
    [InlineData("https://twitch.tv@example.com/activate")]
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

    [Theory]
    [InlineData("device_code", "null")]
    [InlineData("device_code", "\"\"")]
    [InlineData("user_code", "\"\"")]
    [InlineData("expires_in", "0")]
    [InlineData("expires_in", "-1")]
    [InlineData("expires_in", "\"1800\"")]
    [InlineData("expires_in", "2147483648")]
    [InlineData("interval", "0")]
    [InlineData("interval", "-1")]
    [InlineData("interval", "1.5")]
    public void RejectsIncompleteOrInvalidDeviceAuthorizationFields(string field, string valueJson)
    {
        JObject response = new()
        {
            ["device_code"] = "device-secret",
            ["user_code"] = "ABCD-EFGH",
            ["verification_uri"] = "https://www.twitch.tv/activate",
            ["expires_in"] = 1800,
            ["interval"] = 5
        };
        response[field] = JToken.Parse(valueJson);
        using JsonDocument document = JsonDocument.Parse(response.ToString());

        Assert.False(TwitchOAuthAuthorizer.TryReadDeviceAuth(document.RootElement, out TwitchDeviceAuthorization authorization, out string error));
        Assert.Equal(default, authorization);
        Assert.Contains("incomplete device-authorization information", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void RejectsNonObjectAuthorizationAndValidationResponses(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.False(TwitchOAuthAuthorizer.TryReadDeviceAuth(document.RootElement, out TwitchDeviceAuthorization authorization, out string deviceError));
        Assert.Equal(default, authorization);
        Assert.NotEmpty(deviceError);
        Assert.False(TwitchOAuthAuthorizer.TryReadIdentity(document.RootElement, "client123", out string login, out string identityError));
        Assert.Empty(login);
        Assert.NotEmpty(identityError);
    }
}
