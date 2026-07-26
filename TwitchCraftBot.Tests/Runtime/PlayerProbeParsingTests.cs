using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class PlayerProbeParsingTests
{
    [Theory]
    [InlineData("[Server thread/INFO]: Set Steve's game mode to Survival Mode", "Steve", 0)]
    [InlineData("Set the game mode of Alex to Creative Mode", "Alex", 1)]
    [InlineData("[Rcon]: Set Player_3's game mode to Spectator Mode", "Player_3", 3)]
    public void TryParseGamemodeAnnouncementLine_RecognizesSupportedServerFormats(
        string line,
        string expectedPlayer,
        int expectedGameType)
    {
        bool parsed = BotMainHandler.TryParseGamemodeAnnouncementLine(
            line,
            out string player,
            out int gameType);

        Assert.True(parsed);
        Assert.Equal(expectedPlayer, player);
        Assert.Equal(expectedGameType, gameType);
    }

    [Fact]
    public void TryParseGamemodeAnnouncementLine_RejectsMalformedOrUnrelatedLines()
    {
        string[] lines =
        [
            "",
            "Steve joined the game",
            "Set bad-name's game mode to Survival Mode",
            "Set Steve's game mode to Builder Mode",
            "Set the game mode of Alex Creative Mode"
        ];

        foreach (string line in lines)
        {
            Assert.False(BotMainHandler.TryParseGamemodeAnnouncementLine(
                line,
                out string player,
                out int gameType));
            Assert.Equal(string.Empty, player);
            Assert.Equal(-1, gameType);
        }
    }
}
