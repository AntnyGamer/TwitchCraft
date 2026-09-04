using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Authentication;

public sealed class StartupAuthorizationPrerequisiteTests
{
    [Fact]
    public void CanStart_RequiresValidFieldsAndAuthorization()
    {
        Assert.True(SetupInputValidator.CanStart(
            "1.21.8",
            "127.0.0.1",
            "client-id",
            "client-id",
            "access-token",
            "streamer_name",
            "bot_name"));
    }

    [Theory]
    [InlineData("", "127.0.0.1", "client-id", "client-id", "access-token", "streamer", "bot")]
    [InlineData("1.21.8", "not-an-ip", "client-id", "client-id", "access-token", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "", "", "", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "new-client", "old-client", "access-token", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "client-id", "client-id", "", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "client-id", "client-id", "access-token", "bad channel", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "client-id", "client-id", "access-token", "streamer", "")]
    public void CanStart_RejectsMissingOrInvalidRequiredValues(
        string version,
        string bindIp,
        string clientId,
        string authorizedClientId,
        string token,
        string channel,
        string botName)
    {
        Assert.False(SetupInputValidator.CanStart(
            version,
            bindIp,
            clientId,
            authorizedClientId,
            token,
            channel,
            botName));
    }
}
