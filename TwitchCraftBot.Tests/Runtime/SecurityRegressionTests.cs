using System.Reflection;
using System.Runtime.InteropServices;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class SecurityRegressionTests
{
    [Fact]
    public void SecureRandomHelpers_StayWithinTheirRequestedRanges()
    {
        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(BotMainHandler.SecureRandomInt(-4, 5), -4, 4);
            Assert.InRange(BotMainHandler.SecureRandomDouble(), 0, Math.BitDecrement(1));
        }

        Assert.False(BotMainHandler.SecureRandomChance(0));
        Assert.True(BotMainHandler.SecureRandomChance(1));
    }

    [Theory]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(double.PositiveInfinity, true)]
    [InlineData(double.NaN, false)]
    public void SecureRandomChance_HandlesBoundaryProbabilities(double probability, bool expected)
    {
        Assert.Equal(expected, BotMainHandler.SecureRandomChance(probability));
    }

    [Fact]
    public void RestartManagerImports_AreRestrictedToSystem32()
    {
        MethodInfo[] imports = typeof(BotMainHandler)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("RM", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, imports.Length);
        Assert.All(imports, method =>
            Assert.Equal(
                DllImportSearchPath.System32,
                method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths));
    }
}
