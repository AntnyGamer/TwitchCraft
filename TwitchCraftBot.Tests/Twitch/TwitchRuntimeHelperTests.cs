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
    [InlineData("", 4, "")]
    [InlineData("abc", 0, "")]
    [InlineData("é", 1, "")]
    [InlineData("é", 2, "é")]
    [InlineData("😀", 3, "")]
    [InlineData("😀", 4, "😀")]
    [InlineData("Aé", 2, "A")]
    [InlineData("abcdef", 4, "abcd")]
    [InlineData("ééé", 4, "éé")]
    [InlineData("A😀B", 4, "A")]
    [InlineData("A😀B", 5, "A😀")]
    [InlineData("A😀B", 6, "A😀B")]
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

    [Theory]
    [InlineData(true, 1, 1)]
    [InlineData(true, 250, 250)]
    [InlineData(false, 250, 0)]
    [InlineData(true, 0, 0)]
    public void CalculateBitTokenReward_PreservesOneBitPerToken(bool enabled, int bits, int expected)
    {
        Assert.Equal(expected, BotMainHandler.CalculateBitTokenReward(enabled, bits));
    }

    [Theory]
    [InlineData(5, 0.5, 3)]
    [InlineData(10, 1.0, 10)]
    [InlineData(10, 1.5, 15)]
    [InlineData(10, double.NaN, 10)]
    public void CalculateCommandCost_AppliesMultiplierAndRoundsUp(long cost, double multiplier, int expected)
    {
        Assert.Equal(expected, BotMainHandler.CalculateCommandCost(cost, multiplier));
    }

    [Fact]
    public void BotResponseVerbosity_FiltersOnlyTheRequestedResponseKinds()
    {
        Assert.True(BotResponseVerbositySettings.ShouldSend("Normal", BotResponseKind.Confirmation));
        Assert.False(BotResponseVerbositySettings.ShouldSend("Reduced", BotResponseKind.Confirmation));
        Assert.True(BotResponseVerbositySettings.ShouldSend("Reduced", BotResponseKind.Announcement));
        Assert.True(BotResponseVerbositySettings.ShouldSend("Essential Only", BotResponseKind.Essential));
        Assert.False(BotResponseVerbositySettings.ShouldSend("Essential Only", BotResponseKind.Announcement));
    }

    [Theory]
    [InlineData("!heal", "!", "?", "!")]
    [InlineData("??heal", "?", "??", "??")]
    [InlineData("?heal", "!", "?", "?")]
    public void TryMatchCommandPrefix_UsesConfiguredPrefixesAndPrefersTheLongest(
        string payload,
        string primary,
        string secondary,
        string expected)
    {
        Assert.True(BotMainHandler.TryMatchCommandPrefix(payload, primary, secondary, out string actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BotReplyMention_ReplacesAnExistingLeadingUsernameWithoutDuplicatingIt()
    {
        Assert.Equal(
            "@viewer, you need more tokens.",
            BotMainHandler.FormatBotReplyForViewer("viewer, you need more tokens.", "Viewer", mentionViewer: true));
        Assert.Equal(
            "@viewer Unknown command.",
            BotMainHandler.FormatBotReplyForViewer("Unknown command.", "viewer", mentionViewer: true));
    }

    [Fact]
    public void MinecraftRelayMessage_AddsOptionalLocalTimestamp()
    {
        DateTime time = new(2026, 8, 27, 13, 5, 0);

        Assert.Equal("viewer: hello", BotMainHandler.FormatMinecraftRelayMessage("viewer", "hello", false, time));
        Assert.Equal("[13:05] viewer: hello", BotMainHandler.FormatMinecraftRelayMessage("viewer", "hello", true, time));
    }

    [Fact]
    public void ConfiguredPrefix_RewritesCommandExamplesWithoutChangingExclamations()
    {
        Assert.Equal(
            "Use ?heal or ?tokens. Great! You are ready!",
            BotMainHandler.ApplyConfiguredCommandPrefix("Use !heal or !tokens. Great! You are ready!", "?"));
    }
}
