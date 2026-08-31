using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot.Tests.Revamped.Economy;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class ScaleCooldownIsolationTests
{
    [Fact]
    public void TinyAndGiantHaveIndependentFiveMinuteGlobalCooldowns()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.True(runtime.TryUseScaleCommand("tiny", out TimeSpan firstRemaining, out DateTime tinyReservation));
            Assert.Equal(TimeSpan.Zero, firstRemaining);
            Assert.NotEqual(DateTime.MinValue, tinyReservation);

            Assert.False(runtime.TryUseScaleCommand("TINY", out TimeSpan tinyRemaining, out _));
            Assert.InRange(tinyRemaining, TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5));

            Assert.True(runtime.TryUseScaleCommand("giant", out TimeSpan giantRemaining, out _));
            Assert.Equal(TimeSpan.Zero, giantRemaining);
        }
        finally
        {
            runtime.CloseTokenStore();
        }
    }

    [Fact]
    public void FailedDispatchCanReleaseOnlyItsOwnCooldownReservation()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.True(runtime.TryUseScaleCommand("tiny", out _, out DateTime failedReservation));
            runtime.ClearScaleCooldown("tiny", failedReservation);
            Assert.True(runtime.TryUseScaleCommand("tiny", out _, out DateTime activeReservation));

            runtime.ClearScaleCooldown("tiny", failedReservation);
            Assert.False(runtime.TryUseScaleCommand("tiny", out _, out _));

            runtime.ClearScaleCooldown("tiny", activeReservation);
            Assert.True(runtime.TryUseScaleCommand("tiny", out _, out _));
        }
        finally
        {
            runtime.CloseTokenStore();
        }
    }
}
