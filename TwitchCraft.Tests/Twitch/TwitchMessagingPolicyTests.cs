using System;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

public sealed class TwitchMessagingPolicyTests
{
    [Theory]
    [InlineData(" hello ", "hello")]
    [InlineData(" line one\r\nline two ", "line one  line two")]
    public void CleanChannelMessage_TrimsAndRemovesProtocolLineBreaks(
        string message,
        string expected)
    {
        Assert.Equal(expected, MainHandler.CleanChannelMessage(message));
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
    public void TruncateUTF8_RespectsByteLimitsWithoutSplittingUnicode(
        string message,
        int maxBytes,
        string expected)
    {
        Assert.Equal(expected, MainHandler.TruncateUTF8(message, maxBytes));
    }

    [Theory]
    [InlineData("NightBot", "mybot", false, true)]
    [InlineData("StreamLabs", "mybot", false, true)]
    [InlineData("StreamElements", "mybot", false, true)]
    [InlineData("mybot", "MyBot", true, true)]
    [InlineData("mybot", "MyBot", false, false)]
    public void IsIgnoredUser_HandlesKnownServicesAndSeparateBotAccount(
        string sender,
        string botName,
        bool separateBotAccount,
        bool expected)
    {
        Assert.Equal(expected, MainHandler.IsIgnoredUser(sender, botName, separateBotAccount));
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
            MainHandler.GetReconnectDelayMs(currentDelay));
    }

    [Theory]
    [InlineData(true, 250, 250)]
    [InlineData(false, 250, 0)]
    [InlineData(true, 0, 0)]
    public void GetBitReward_PreservesOneBitPerToken(bool enabled, int bits, int expected)
    {
        Assert.Equal(expected, MainHandler.GetBitReward(enabled, bits));
    }

    [Theory]
    [InlineData("Normal", nameof(BotResponseKind.Confirmation), true)]
    [InlineData("Reduced", nameof(BotResponseKind.Confirmation), false)]
    [InlineData("Reduced", nameof(BotResponseKind.Announcement), true)]
    [InlineData("Essential Only", nameof(BotResponseKind.Essential), true)]
    [InlineData("Essential Only", nameof(BotResponseKind.Announcement), false)]
    public void ShouldSend_AllowsOnlyConfiguredResponseKinds(
        string verbosity,
        string kind,
        bool expected)
    {
        Assert.Equal(
            expected,
            BotResponseVerbositySettings.ShouldSend(verbosity, Enum.Parse<BotResponseKind>(kind)));
    }

    [Theory]
    [InlineData(10, 0.0, 0)]
    [InlineData(5, 0.5, 3)]
    [InlineData(long.MaxValue, 5.0, int.MaxValue)]
    [InlineData(10, double.NaN, 10)]
    public void GetCommandCost_HandlesBoundsMultiplierAndRounding(long cost, double multiplier, int expected)
    {
        Assert.Equal(expected, CommandService.GetCommandCost(cost, multiplier));
    }

    [Theory]
    [InlineData("!heal", "!", "?", true, "!")]
    [InlineData("??heal", "?", "??", true, "??")]
    [InlineData("heal", "!", "?", false, "")]
    public void TryMatchPrefix_UsesConfiguredPrefixesAndPrefersTheLongest(
        string payload,
        string primary,
        string secondary,
        bool expectedMatch,
        string expectedPrefix)
    {
        Assert.Equal(expectedMatch, MainHandler.TryMatchPrefix(payload, primary, secondary, out string actual));
        Assert.Equal(expectedPrefix, actual);
    }

    [Fact]
    public void FormatReply_ReplacesLeadingUsernameOrAddsMention()
    {
        Assert.Equal(
            "@viewer, you need more tokens.",
            MainHandler.FormatReply("viewer, you need more tokens.", "Viewer", mentionViewer: true));
        Assert.Equal(
            "@viewer Unknown command.",
            MainHandler.FormatReply("Unknown command.", "viewer", mentionViewer: true));
    }

    [Fact]
    public void FormatRelay_AddsOptionalLocalTimestamp()
    {
        DateTime time = new(2026, 8, 27, 13, 5, 0);

        Assert.Equal("viewer: hello", MainHandler.FormatRelay("viewer", "hello", false, time));
        Assert.Equal("[13:05] viewer: hello", MainHandler.FormatRelay("viewer", "hello", true, time));
    }

    [Fact]
    public void ApplyPrefix_RewritesCommandExamplesWithoutChangingExclamations()
    {
        Assert.Equal(
            "Use ?heal or ?tokens. Great! You are ready!",
            MainHandler.ApplyPrefix("Use !heal or !tokens. Great! You are ready!", "?"));
    }
}
