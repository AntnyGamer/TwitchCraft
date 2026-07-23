using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Statistics;

public sealed class StatisticsNormalizationTests
{
    [Theory]
    [InlineData(" !HEAL ", "heal")]
    [InlineData("!   GambleTokens ", "gambletokens")]
    [InlineData(" \t ! \t ", "")]
    public void NormalizeCommandName_ProducesStableStatisticsKey(string value, string expected)
    {
        Assert.Equal(expected, StatisticNameHelper.NormalizeCommandName(value));
    }
}
