using TwitchCraft_V1;

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
}
