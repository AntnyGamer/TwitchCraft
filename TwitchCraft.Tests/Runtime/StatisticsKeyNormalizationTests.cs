using System.Collections.Generic;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class StatisticsKeyNormalizationTests
{
    [Theory]
    [InlineData(" !HEAL ", "heal")]
    [InlineData("!   GambleTokens ", "gambletokens")]
    [InlineData(" \t ! \t ", "")]
    public void CleanCommandName_ProducesNormalizedStatisticsKey(string value, string expected)
    {
        Assert.Equal(expected, StatisticNameHelper.CleanCommandName(value));
    }

    [Fact]
    public void Normalize_MergesCommandAliasesAndDropsInvalidCounts()
    {
        CommandStatisticsBucket statistics = new()
        {
            CommandUseCounts = new Dictionary<string, long>
            {
                [" !HEAL "] = 2,
                ["heal"] = 3,
                [" "] = 4,
                ["negative"] = -1,
                ["zero"] = 0
            }
        };

        statistics.Normalize();

        Assert.Equal(new KeyValuePair<string, long>("heal", 5), Assert.Single(statistics.CommandUseCounts));
        Assert.Equal(5, statistics.CommandUseCounts["HEAL"]);
    }

    [Fact]
    public void LifetimeNormalize_ClampsCorruptCountersAndRepairsMissingCommandCounts()
    {
        LifetimeStatistics statistics = new()
        {
            GameCommandsRun = -1,
            TokensSpent = 15,
            EffectsGiven = -2,
            Deaths = -3,
            LastDeathScore = -4,
            SessionsStarted = -5,
            LongestSurvivalSeconds = -6,
            ShortestSurvivalSeconds = -7,
            CommandUseCounts = null!
        };

        statistics.Normalize();

        Assert.Equal(0, statistics.GameCommandsRun);
        Assert.Equal(15, statistics.TokensSpent);
        Assert.Equal(0, statistics.EffectsGiven);
        Assert.Equal(0, statistics.Deaths);
        Assert.Equal(0, statistics.LastDeathScore);
        Assert.Equal(0, statistics.SessionsStarted);
        Assert.Equal(0, statistics.LongestSurvivalSeconds);
        Assert.Equal(0, statistics.ShortestSurvivalSeconds);
        Assert.Empty(statistics.CommandUseCounts);
        statistics.CommandUseCounts["heal"] = 1;
        Assert.Equal(1, statistics.CommandUseCounts["HEAL"]);
    }
}
