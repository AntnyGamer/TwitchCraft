using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Twitch;

public sealed class TwitchMessagingPolicyTests
{
    [Theory]
    [InlineData(" hello ", "hello")]
    [InlineData(" line one\r\nline two ", "line one  line two")]
    public void CleanChannelMessage_TrimsAndRemovesProtocolLineBreaks(
        string message,
        string expected)
    {
        Assert.Equal(expected, BotMainHandler.CleanChannelMessage(message));
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
    public void TruncateUtf8_RespectsByteLimitsWithoutSplittingUnicode(
        string message,
        int maxBytes,
        string expected)
    {
        Assert.Equal(expected, BotMainHandler.TruncateUtf8(message, maxBytes));
    }

    [Theory]
    [InlineData("NightBot", "mybot", false)]
    [InlineData("mybot", "MyBot", true)]
    public void IsIgnoredUser_RecognizesServicesAndSeparateBotAccount(
        string sender,
        string botName,
        bool separateBotAccount)
    {
        Assert.True(BotMainHandler.IsIgnoredUser(sender, botName, separateBotAccount));
    }

    [Theory]
    [InlineData(1000, 2000)]
    [InlineData(8000, 15000)]
    [InlineData(15000, 15000)]
    public void GetReconnectDelayMs_DoublesAndCapsBackoff(
        int currentDelay,
        int expected)
    {
        Assert.Equal(
            expected,
            BotMainHandler.GetReconnectDelayMs(currentDelay));
    }

    [Theory]
    [InlineData(true, 1, 1)]
    [InlineData(true, 250, 250)]
    [InlineData(false, 250, 0)]
    [InlineData(true, 0, 0)]
    public void GetBitReward_PreservesOneBitPerToken(bool enabled, int bits, int expected)
    {
        Assert.Equal(expected, BotMainHandler.GetBitReward(enabled, bits));
    }

    [Theory]
    [InlineData("Normal", nameof(BotResponseKind.Confirmation), true)]
    [InlineData("Reduced", nameof(BotResponseKind.Confirmation), false)]
    [InlineData("Reduced", nameof(BotResponseKind.Announcement), true)]
    [InlineData("Essential Only", nameof(BotResponseKind.Essential), true)]
    [InlineData("Essential Only", nameof(BotResponseKind.Announcement), false)]
    public void ResponseVerbosity_SendsOnlyTheConfiguredResponseKinds(
        string verbosity,
        string kind,
        bool expected)
    {
        Assert.Equal(
            expected,
            BotResponseVerbositySettings.ShouldSend(verbosity, Enum.Parse<BotResponseKind>(kind)));
    }

    [Theory]
    [InlineData(5, 0.5, 3)]
    [InlineData(10, 1.0, 10)]
    [InlineData(10, 1.5, 15)]
    [InlineData(10, double.NaN, 10)]
    public void GetCommandCost_AppliesMultiplierAndRoundsUp(long cost, double multiplier, int expected)
    {
        Assert.Equal(expected, CommandService.GetCommandCost(cost, multiplier));
    }

    [Theory]
    [InlineData("!heal", "!", "?", "!")]
    [InlineData("??heal", "?", "??", "??")]
    [InlineData("?heal", "!", "?", "?")]
    public void TryMatchPrefix_UsesConfiguredPrefixesAndPrefersTheLongest(
        string payload,
        string primary,
        string secondary,
        string expected)
    {
        Assert.True(BotMainHandler.TryMatchPrefix(payload, primary, secondary, out string actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BotReplyMention_ReplacesAnExistingLeadingUsernameWithoutDuplicatingIt()
    {
        Assert.Equal(
            "@viewer, you need more tokens.",
            BotMainHandler.FormatReply("viewer, you need more tokens.", "Viewer", mentionViewer: true));
        Assert.Equal(
            "@viewer Unknown command.",
            BotMainHandler.FormatReply("Unknown command.", "viewer", mentionViewer: true));
    }

    [Fact]
    public void MinecraftRelayMessage_AddsOptionalLocalTimestamp()
    {
        DateTime time = new(2026, 8, 27, 13, 5, 0);

        Assert.Equal("viewer: hello", BotMainHandler.FormatRelay("viewer", "hello", false, time));
        Assert.Equal("[13:05] viewer: hello", BotMainHandler.FormatRelay("viewer", "hello", true, time));
    }

    [Fact]
    public void ConfiguredPrefix_RewritesCommandExamplesWithoutChangingExclamations()
    {
        Assert.Equal(
            "Use ?heal or ?tokens. Great! You are ready!",
            BotMainHandler.ApplyPrefix("Use !heal or !tokens. Great! You are ready!", "?"));
    }
}
