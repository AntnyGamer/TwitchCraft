using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Persistence;

public sealed class StatisticsKeyNormalizationTests
{
    [Theory]
    [InlineData(" !HEAL ", "heal")]
    [InlineData("!   GambleTokens ", "gambletokens")]
    [InlineData(" \t ! \t ", "")]
    public void CleanCommandName_ProducesStableStatisticsKey(string value, string expected)
    {
        Assert.Equal(expected, StatisticNameHelper.CleanCommandName(value));
    }
}
