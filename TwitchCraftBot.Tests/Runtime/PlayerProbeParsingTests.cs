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
}
