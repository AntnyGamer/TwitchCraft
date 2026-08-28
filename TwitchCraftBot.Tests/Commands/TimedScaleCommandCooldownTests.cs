using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot.Tests.Tokens;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Commands;

[Collection(SqliteDatabaseTestCollection.Name)]
public sealed class TimedScaleCommandCooldownTests
{
    [Fact]
    public void TinyAndGiantHaveIndependentFiveMinuteGlobalCooldowns()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);

        try
        {
            Assert.True(runtime.TryUseTimedScaleCommand("tiny", out TimeSpan firstRemaining, out DateTime tinyReservation));
            Assert.Equal(TimeSpan.Zero, firstRemaining);
            Assert.NotEqual(DateTime.MinValue, tinyReservation);

            Assert.False(runtime.TryUseTimedScaleCommand("TINY", out TimeSpan tinyRemaining, out _));
            Assert.InRange(tinyRemaining, TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5));

            Assert.True(runtime.TryUseTimedScaleCommand("giant", out TimeSpan giantRemaining, out _));
            Assert.Equal(TimeSpan.Zero, giantRemaining);
        }
        finally
        {
            runtime.CloseTokenStoreConnection();
        }
    }

    [Fact]
    public void FailedDispatchCanReleaseOnlyItsOwnCooldownReservation()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);

        try
        {
            Assert.True(runtime.TryUseTimedScaleCommand("tiny", out _, out DateTime failedReservation));
            runtime.ClearTimedScaleCommandCooldown("tiny", failedReservation);
            Assert.True(runtime.TryUseTimedScaleCommand("tiny", out _, out DateTime activeReservation));

            runtime.ClearTimedScaleCommandCooldown("tiny", failedReservation);
            Assert.False(runtime.TryUseTimedScaleCommand("tiny", out _, out _));

            runtime.ClearTimedScaleCommandCooldown("tiny", activeReservation);
            Assert.True(runtime.TryUseTimedScaleCommand("tiny", out _, out _));
        }
        finally
        {
            runtime.CloseTokenStoreConnection();
        }
    }
}
