using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

public sealed class OAuthTokenFormattingTests
{
    [Theory]
    [InlineData(" oauth:secret ", "secret")]
    [InlineData("OAUTH:secret", "secret")]
    [InlineData("oauth:   secret", "secret")]
    [InlineData("secret", "secret")]
    [InlineData("\tOaUtH:\u2003secret\r\n", "secret")]
    [InlineData("oauth:", "")]
    [InlineData("oauth:   ", "")]
    [InlineData(" \t\r\n", "")]
    [InlineData(null, "")]
    public void NormalizeAccessToken_RemovesWhitespaceAndOAuthPrefix(string? value, string expected)
    {
        Assert.Equal(expected, TwitchTokenHelper.NormalizeAccessToken(value));
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("oauth:secret")]
    [InlineData(" OAUTH:  secret ")]
    public void HeaderBuilders_AddExactlyOneProtocolPrefix(string token)
    {
        Assert.Equal("oauth:secret", TwitchTokenHelper.BuildIRCPassword(token));
        Assert.Equal("Bearer secret", TwitchTokenHelper.BuildBearerHeader(token));
        Assert.Equal("OAuth secret", TwitchTokenHelper.BuildValidateHeader(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("oauth:")]
    public void HeaderBuilders_LeaveMissingCredentialsEmpty(string? token)
    {
        Assert.Empty(TwitchTokenHelper.BuildIRCPassword(token));
        Assert.Empty(TwitchTokenHelper.BuildBearerHeader(token));
        Assert.Empty(TwitchTokenHelper.BuildValidateHeader(token));
    }
}
