using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Twitch;

public sealed class OAuthTokenFormattingTests
{
    [Theory]
    [InlineData(" oauth:secret ", "secret")]
    [InlineData("OAUTH:secret", "secret")]
    [InlineData("secret", "secret")]
    [InlineData(null, "")]
    public void NormalizeAccessToken_RemovesWhitespaceAndOAuthPrefix(string? value, string expected)
    {
        Assert.Equal(expected, TwitchTokenHelper.NormalizeAccessToken(value));
    }

    [Fact]
    public void HeaderBuilders_AddExactlyOneProtocolPrefix()
    {
        Assert.Equal("oauth:secret", TwitchTokenHelper.BuildIrcPassword("oauth:secret"));
        Assert.Equal("Bearer secret", TwitchTokenHelper.BuildBearerHeader("oauth:secret"));
        Assert.Equal("OAuth secret", TwitchTokenHelper.BuildValidateHeader("oauth:secret"));
    }
}
