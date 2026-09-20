using System;
using System.IO;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft.Tests.Economy;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class ScaleCooldownIsolationTests
{
    [Fact]
    public void TinyAndGiantHaveIndependentFiveMinuteGlobalCooldowns()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            const long now = 1_000_000;
            Assert.True(runtime.Commands.TryUseScaleCommand("tiny", out TimeSpan firstRemaining, out long tinyReservation, now));
            Assert.Equal(TimeSpan.Zero, firstRemaining);
            Assert.Equal(now, tinyReservation);

            Assert.False(runtime.Commands.TryUseScaleCommand("TINY", out TimeSpan tinyRemaining, out _, now));
            Assert.Equal(TimeSpan.FromMinutes(5), tinyRemaining);

            Assert.True(runtime.Commands.TryUseScaleCommand("giant", out TimeSpan giantRemaining, out _, now));
            Assert.Equal(TimeSpan.Zero, giantRemaining);
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public void ClearScaleCooldown_OnlyClearsMatchingReservation()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.True(runtime.Commands.TryUseScaleCommand("tiny", out _, out long failedReservation));
            runtime.Commands.ClearScaleCooldown("tiny", failedReservation);
            Assert.True(runtime.Commands.TryUseScaleCommand("tiny", out _, out long activeReservation));

            runtime.Commands.ClearScaleCooldown("tiny", failedReservation);
            Assert.False(runtime.Commands.TryUseScaleCommand("tiny", out _, out _));

            runtime.Commands.ClearScaleCooldown("tiny", activeReservation);
            Assert.True(runtime.Commands.TryUseScaleCommand("tiny", out _, out _));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
