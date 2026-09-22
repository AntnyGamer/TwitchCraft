using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.Economy;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class GameplayCommandIntegrationTests
{
    [Fact]
    public Task Effect_MultipleRollsChargePerEffectAndDispatchEveryRoll()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!effect 3", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            Assert.Equal(97, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(3, commands.Count(static line =>
                line.StartsWith("effect give @a[gamemode=!spectator] minecraft:", StringComparison.Ordinal)));
            Assert.Equal(3, commands.Count(static line =>
                line.StartsWith("tellraw @a[gamemode=!spectator] ", StringComparison.Ordinal) &&
                line.Contains("viewer gave you", StringComparison.OrdinalIgnoreCase)));
        });

    [Fact]
    public Task Effect_InvalidCountsNeverChargeOrReachMinecraft()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!effect 0", "viewer", cancellationToken);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!effect 26", "viewer", cancellationToken);

            Assert.Equal(100, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(0, ReadCommands(config).Count(static line =>
                line.StartsWith("effect give @a[gamemode=!spectator] minecraft:", StringComparison.Ordinal)));
        });

    [Fact]
    public Task ChargedCreeper_ChargesOnceAndDispatchesTheCompletePursuerSequence()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!chargedcreeper", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            Assert.Equal(55, runtime.Tokens.GetBalance("viewer"));
            Assert.True(commands.Any(static line =>
                line.Contains("summon minecraft:creeper", StringComparison.Ordinal) &&
                line.Contains("powered:1b", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("effect give @s minecraft:glowing 255 0 true", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("run tag @s remove tc_charged_creeper_new_", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.StartsWith("title @a[gamemode=!spectator] times ", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("A charged creeper is coming!", StringComparison.Ordinal)));
        });

    [Fact]
    public Task Scared_ChargesOnceAndSpawnsExactlyTwentyCats()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!scared", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            Assert.Equal(85, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(20, commands.Count(static line => line.Contains("summon minecraft:cat", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("Scaredy Cat!", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("Stop being scared!", StringComparison.Ordinal)));
        });

    [Fact]
    public Task Slaughter_DisablesMobDropsOnlyAroundTheKillAndAlwaysRestoresThem()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!slaughter", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            int kill = commands.FindIndex(static line =>
                line.Contains("type=!minecraft:player", StringComparison.Ordinal) &&
                line.EndsWith("run kill @s", StringComparison.Ordinal));
            Assert.Equal(70, runtime.Tokens.GetBalance("viewer"));
            Assert.True(kill > 0 && kill + 1 < commands.Count);
            Assert.StartsWith("gamerule ", commands[kill - 1], StringComparison.Ordinal);
            Assert.EndsWith(" false", commands[kill - 1], StringComparison.Ordinal);
            Assert.Equal(commands[kill - 1][..^6] + " true", commands[kill + 1]);
        });

    [Fact]
    public Task SwitchMilk_UsesOneUniqueTagForTheWholeAtomicSequence()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!switchmilk", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            string first = Assert.Single(commands.Where(static line =>
                line.StartsWith("tag @a remove tc_switchmilk_", StringComparison.Ordinal)));
            string tag = first[(first.LastIndexOf(' ') + 1)..];
            List<string> tagged = commands.Where(line => line.Contains(tag, StringComparison.Ordinal)).ToList();

            Assert.Equal(94, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(6, tagged.Count);
            Assert.True(tagged.Any(static line => line.Contains("Inventory[{id:\"minecraft:milk_bucket\"}]", StringComparison.Ordinal)));
            Assert.True(tagged.Any(static line =>
                line.Contains("run give @s minecraft:bucket 1", StringComparison.Ordinal) ||
                line.Contains("run give @s minecraft:water_bucket 1", StringComparison.Ordinal) ||
                line.Contains("run give @s minecraft:lava_bucket 1", StringComparison.Ordinal)));
            Assert.EndsWith("remove " + tag, tagged[^1], StringComparison.Ordinal);
        });

    [Fact]
    public Task Mlg_ChargesOnceAndDispatchesBothDimensionSafeBranches()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 200);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!mlg", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            Assert.Equal(50, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(5, commands.Count(static line => line.Contains("dimension minecraft:the_nether", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("if dimension minecraft:the_nether run fill", StringComparison.Ordinal)));
            Assert.True(commands.Any(static line => line.Contains("unless dimension minecraft:the_nether run give @s minecraft:water_bucket 1", StringComparison.Ordinal)));
        });

    [Fact]
    public Task Swarm_ChargesOnceAndUsesFiveDistinctMobs()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!swarm", "viewer", cancellationToken);

            List<string> summons = ReadCommands(config)
                .Where(static line => line.StartsWith("execute at @a[gamemode=!spectator] run summon minecraft:", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(55, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(5, summons.Count);
            Assert.Equal(5, summons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });

    [Fact]
    public Task HealAll_ExcludesSpectatorsAndScalesCostToTargetablePlayers()
        => RunAsync(
            async (runtime, config, cancellationToken) =>
            {
                runtime.Tokens.Award("viewer", 100);
                await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!heal all", "viewer", cancellationToken);

                Assert.Equal(96, runtime.Tokens.GetBalance("viewer"));
                Assert.Equal(1, ReadCommands(config).Count(static line =>
                    string.Equals(line, "effect give @a[gamemode=!spectator] minecraft:instant_health 1 1", StringComparison.Ordinal)));
            },
            config =>
            {
                config.Settings.MultiplayerEnabled = true;
                FakeJavaServer.SetProbePlayers(config, ("streamer", 0), ("other", 0), ("spectator", 3));
            });

    [Fact]
    public Task Heal_OfflineNamedTargetDoesNotChargeOrDispatch()
        => RunAsync(
            async (runtime, config, cancellationToken) =>
            {
                runtime.Tokens.Award("viewer", 100);
                await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!heal MissingPlayer", "viewer", cancellationToken);

                Assert.Equal(100, runtime.Tokens.GetBalance("viewer"));
                Assert.Equal(0, ReadCommands(config).Count(static line =>
                    line.Contains("minecraft:instant_health", StringComparison.Ordinal)));
            },
            config =>
            {
                config.Settings.MultiplayerEnabled = true;
                FakeJavaServer.SetProbePlayers(config, ("streamer", 0));
            });

    [Fact]
    public Task Tiny_ChargesAndAppliesTheScaleImmediately()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 100);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!tiny", "viewer", cancellationToken);

            Assert.Equal(80, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(1, ReadCommands(config).Count(static line =>
                line.Contains("minecraft:scale base set 0.5", StringComparison.Ordinal)));
        });

    [Fact]
    public Task HeartCommands_AddModifierChargeOnceAndShareTheFiveMinuteCooldown()
        => RunAsync(async (runtime, config, cancellationToken) =>
        {
            runtime.Tokens.Award("viewer", 300);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!addheart 2", "viewer", cancellationToken);
            await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!removeheart 1", "viewer", cancellationToken);

            List<string> commands = ReadCommands(config);
            Assert.Equal(200, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(1, commands.Count(static line =>
                line.Contains(" modifier add twitchcraft:heart_", StringComparison.Ordinal) &&
                line.EndsWith(" 4 add_value", StringComparison.Ordinal)));
            Assert.Equal(0, commands.Count(static line =>
                line.Contains(" modifier remove twitchcraft:heart_", StringComparison.Ordinal)));
        });

    [Fact]
    public Task HeartCommands_RejectBothUpperAndLowerHealthLimitViolationsWithoutCharging()
        => RunAsync(
            async (runtime, config, cancellationToken) =>
            {
                runtime.Tokens.Award("viewer", 300);
                await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!addheart 1", "viewer", cancellationToken);
                FakeJavaServer.SetProbeHealth(config, 10);
                await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!removeheart 1", "viewer", cancellationToken);

                Assert.Equal(300, runtime.Tokens.GetBalance("viewer"));
                Assert.Equal(0, ReadCommands(config).Count(static line =>
                    line.Contains(" modifier add twitchcraft:heart_", StringComparison.Ordinal)));
            },
            config => FakeJavaServer.SetProbeHealth(config, 40));

    [Fact]
    public Task Rename_RenamesTheHeldItemPreservesItsCountAndChargesOnlyOnSuccess()
        => RunAsync(
            async (runtime, config, cancellationToken) =>
            {
                runtime.Tokens.Award("viewer", 100);
                await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!rename", "viewer", cancellationToken);

                string replace = Assert.Single(ReadCommands(config).Where(static line =>
                    line.StartsWith(
                        "item replace entity @a[name=\"streamer\",gamemode=!spectator] weapon.mainhand with minecraft:diamond_sword[",
                        StringComparison.Ordinal)));
                Assert.Equal(90, runtime.Tokens.GetBalance("viewer"));
                Assert.Contains("minecraft:damage=5", replace, StringComparison.Ordinal);
                Assert.Contains("minecraft:custom_name=", replace, StringComparison.Ordinal);
                Assert.Contains("viewer", replace, StringComparison.Ordinal);
                Assert.EndsWith(" 2", replace, StringComparison.Ordinal);
            },
            config => FakeJavaServer.SetProbeItem(
                config,
                "{id:'minecraft:diamond_sword',count:2,components:{\"minecraft:damage\":5}}"));

    [Fact]
    public Task Enchant_EmptyHandStillRollsTheVanillaFallbackAndChargesOnce()
        => RunAsync(
            async (runtime, config, cancellationToken) =>
            {
                runtime.Tokens.Award("viewer", 100);
                await FakeJavaServer.QueueCommandAndWaitAsync(runtime, "!enchant", "viewer", cancellationToken);

                List<string> commands = ReadCommands(config);
                Assert.Equal(80, runtime.Tokens.GetBalance("viewer"));
                Assert.Equal(1, commands.Count(static line =>
                    line.StartsWith(
                        "enchant @a[name=\"streamer\",gamemode=!spectator] minecraft:",
                        StringComparison.Ordinal)));
                Assert.True(commands.Any(static line =>
                    line.StartsWith("tellraw @a[name=\"streamer\",gamemode=!spectator] ", StringComparison.Ordinal) &&
                    line.Contains("but you were not holding an item", StringComparison.OrdinalIgnoreCase)));
            },
            config => FakeJavaServer.SetProbeItem(config, "{id:'minecraft:air',count:1}"));

    private static async Task RunAsync(
        Func<MainHandler, TwitchCraftConfig, CancellationToken, Task> test,
        Action<TwitchCraftConfig>? configure = null)
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready-probes");
        config.Identity.StreamerMinecraftName = "streamer";
        FakeJavaServer.SetProbePlayers(config, ("streamer", 0));
        configure?.Invoke(config);

        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        try
        {
            await FakeJavaServer.StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            await test(runtime, config, serverCts.Token);
        }
        finally
        {
            serverCts.Cancel();
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    private static List<string> ReadCommands(TwitchCraftConfig config)
        => FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin");
}
