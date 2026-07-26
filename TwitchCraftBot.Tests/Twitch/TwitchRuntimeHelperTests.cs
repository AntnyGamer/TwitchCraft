using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Twitch;

public sealed class TwitchRuntimeHelperTests
{
    [Theory]
    [InlineData(" hello ", "hello")]
    [InlineData(" line one\r\nline two ", "line one  line two")]
    public void NormalizeOutgoingChannelMessage_TrimsAndRemovesProtocolLineBreaks(
        string message,
        string expected)
    {
        Assert.Equal(expected, BotMainHandler.NormalizeOutgoingChannelMessage(message));
    }

    [Theory]
    [InlineData("abcdef", 4, "abcd")]
    [InlineData("ééé", 4, "éé")]
    [InlineData("A😀B", 5, "A😀")]
    public void TruncateUtf8ToByteCount_RespectsByteLimitsWithoutSplittingUnicode(
        string message,
        int maxBytes,
        string expected)
    {
        Assert.Equal(expected, BotMainHandler.TruncateUtf8ToByteCount(message, maxBytes));
    }

    [Theory]
    [InlineData("NightBot", "mybot", false)]
    [InlineData("mybot", "MyBot", true)]
    public void IsIgnoredIrcUser_RecognizesServicesAndTheSeparateBotAccount(
        string sender,
        string botName,
        bool separateBotAccount)
    {
        Assert.True(BotMainHandler.IsIgnoredIRCUser(sender, botName, separateBotAccount));
    }

    [Fact]
    public void QueueLogHelpers_RemoveTagsAndLimitFailureContextToTheCommandName()
    {
        Assert.False(BotMainHandler.IsIgnoredIRCUser("viewer", "mybot", separateBotAccount: true));
        Assert.Equal(
            ":viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #channel :hello",
            BotMainHandler.StripIRCTagsForLog(
                "@badge-info=;badges= :viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #channel :hello"));
        Assert.Equal("command !heal", BotMainHandler.BuildCommandQueueContext("!heal Steve"));
        Assert.Equal("command !heal", BotMainHandler.BuildCommandQueueContext("!heal"));
    }

    [Theory]
    [InlineData(1000, 2000)]
    [InlineData(8000, 15000)]
    [InlineData(15000, 15000)]
    public void GetNextIrcReconnectDelayMilliseconds_DoublesAndCapsBackoff(
        int currentDelay,
        int expected)
    {
        Assert.Equal(
            expected,
            BotMainHandler.GetNextIRCReconnectDelayMilliseconds(currentDelay));
    }
}
