using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using Xunit;

namespace TwitchCraft.Tests.Commands;

public sealed class ScaleCooldownIsolationTests
{
    [Fact]
    public async Task TimedCommands_HaveIndependentFiveMinuteGlobalCooldowns()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken);
        scenario.Runtime.Tokens.Award("viewer", 200);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!tiny");
        await scenario.DispatchAsync("!tiny");
        await scenario.DispatchAsync("!giant");
        await scenario.DispatchAsync("!lightning");
        await scenario.DispatchAsync("!lightning");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(110, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(1, commands.Count(command =>
            command.Contains("minecraft:scale base set 0.5", StringComparison.Ordinal)));
        Assert.Equal(1, commands.Count(command =>
            command.Contains("minecraft:scale base set 2", StringComparison.Ordinal)));
        Assert.Equal(1, commands.Count(command =>
            command.StartsWith("execute at @a[gamemode=!spectator] run summon minecraft:lightning_bolt", StringComparison.Ordinal)));
    }
}
