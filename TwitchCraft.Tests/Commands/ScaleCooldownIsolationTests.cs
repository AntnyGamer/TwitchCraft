using System;
using System.IO;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft.Tests.Economy;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
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
            Assert.True(runtime.Commands.TryUseTimedCommand("tiny", out TimeSpan firstRemaining, out long tinyReservation, now));
            Assert.Equal(TimeSpan.Zero, firstRemaining);
            Assert.Equal(now, tinyReservation);

            Assert.False(runtime.Commands.TryUseTimedCommand("TINY", out TimeSpan tinyRemaining, out _, now));
            Assert.Equal(TimeSpan.FromMinutes(5), tinyRemaining);

            Assert.True(runtime.Commands.TryUseTimedCommand("giant", out TimeSpan giantRemaining, out _, now));
            Assert.Equal(TimeSpan.Zero, giantRemaining);

            Assert.True(runtime.Commands.TryUseTimedCommand("heart", out TimeSpan heartRemaining, out _, now));
            Assert.Equal(TimeSpan.Zero, heartRemaining);
            Assert.False(runtime.Commands.TryUseTimedCommand("heart", out heartRemaining, out _, now));
            Assert.Equal(TimeSpan.FromMinutes(5), heartRemaining);

            Assert.True(runtime.Commands.TryUseTimedCommand("lightning", out TimeSpan lightningRemaining, out _, now));
            Assert.Equal(TimeSpan.Zero, lightningRemaining);

            Assert.True(runtime.Commands.TryUseTimedCommand("clear-test", out _, out long failedReservation, now));
            runtime.Commands.ClearTimedCommandCooldown("clear-test", failedReservation);
            Assert.True(runtime.Commands.TryUseTimedCommand("clear-test", out _, out long activeReservation, now + 1));
            runtime.Commands.ClearTimedCommandCooldown("clear-test", failedReservation);
            Assert.False(runtime.Commands.TryUseTimedCommand("clear-test", out _, out _, now + 2));
            runtime.Commands.ClearTimedCommandCooldown("clear-test", activeReservation);
            Assert.True(runtime.Commands.TryUseTimedCommand("clear-test", out _, out _, now + 3));

            TwitchCraftConfig config = new();
            config.Settings.CommandCustomizations["addheart"] = new() { GlobalCooldownSeconds = 1 };
            runtime.Commands.SetContext(config);
            runtime.Commands.ResetCommandState();
            runtime.Commands.BeginCommand("addheart", false);
            Assert.True(runtime.Commands.TryUseTimedCommand("heart", out _, out _, now));
            runtime.Commands.EndCommand();
            runtime.Commands.BeginCommand("removeheart", false);
            Assert.True(runtime.Commands.TryUseTimedCommand("heart", out _, out _, now));
            Assert.False(runtime.Commands.TryUseTimedCommand("heart", out TimeSpan overrideRemaining, out _, now));
            Assert.Equal(TimeSpan.FromMinutes(5), overrideRemaining);
            runtime.Commands.EndCommand();
        }
        finally
        {
            runtime.Commands.EndCommand();
            runtime.Tokens.Close();
        }
    }
}
