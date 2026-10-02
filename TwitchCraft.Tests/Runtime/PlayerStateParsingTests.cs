using System.Collections.Generic;
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
    }

    [Theory]
    [InlineData("[Server thread/INFO]: System chat: Steve fell from a high place", true, true)]
    [InlineData("[Server thread/INFO]: [System] [CHAT] Steve was slain by Zombie", true, true)]
    [InlineData("Steve drowned", true, true)]
    [InlineData("[Rcon]: Steve was shot by Alex", true, true)]
    [InlineData("Alex was slain by Steve", true, false)]
    [InlineData("SteveX died", true, false)]
    [InlineData("<Steve> Steve died", true, false)]
    [InlineData("Steve joined the game", true, false)]
    [InlineData("Steve drowned", false, false)]
    public void Statistics_QueuesDeathScoreOnlyForTheTrackedVictim(string line, bool enabled, bool expectedRefresh)
    {
        List<string> refreshedPlayers = [];
        StatisticsService statistics = new(new(
            _ => ChatCommandStatisticFlags.None, _ => true, _ => false,
            () => { }, () => { }, () => { }, refreshedPlayers.Add, _ => { }));
        statistics.SetContext(enabled, "streamer", "Steve", "!");

        statistics.RecordLine(line, hasDeathScoreObjective: false);

        Assert.Equal(expectedRefresh ? new[] { "Steve" } : [], refreshedPlayers);
    }
}
