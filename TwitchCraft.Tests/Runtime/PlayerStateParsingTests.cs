using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class PlayerStateParsingTests
{
    [Theory]
    [InlineData("[Server thread/INFO]: Set Steve's game mode to Survival Mode", "Steve", 0)]
    [InlineData("[Server thread/INFO]: [System] [CHAT] Set Steve's game mode to Survival Mode", "Steve", 0)]
    [InlineData("Set the game mode of Alex to Creative Mode", "Alex", 1)]
    [InlineData("[Rcon]: Set Player_3's game mode to Spectator Mode", "Player_3", 3)]
    public void TryParseGamemode_RecognizesSupportedServerFormats(
        string line,
        string expectedPlayer,
        int expectedGameType)
    {
        bool parsed = MainHandler.TryParseGamemode(
            line,
            out string player,
            out int gameType);

        Assert.True(parsed);
        Assert.Equal(expectedPlayer, player);
        Assert.Equal(expectedGameType, gameType);
    }

    [Fact]
    public void TryParseGamemode_RejectsMalformedOrUnrelatedLines()
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
            Assert.False(MainHandler.TryParseGamemode(
                line,
                out string player,
                out int gameType));
            Assert.Equal(string.Empty, player);
            Assert.Equal(-1, gameType);
        }

        int deathScoreRefreshes = 0;
        StatisticsService statistics = new(new(
            _ => ChatCommandStatisticFlags.None, _ => true, _ => false,
            () => { }, () => { }, () => { }, _ => deathScoreRefreshes++, _ => { }));
        statistics.SetContext(true, "streamer", "Steve", "!");
        statistics.RecordLine("[Server thread/INFO]: System chat: Steve fell from a high place", false);
        statistics.RecordLine("[Server thread/INFO]: [System] [CHAT] Steve was slain by Zombie", false);
        Assert.Equal(2, deathScoreRefreshes);
    }
}
