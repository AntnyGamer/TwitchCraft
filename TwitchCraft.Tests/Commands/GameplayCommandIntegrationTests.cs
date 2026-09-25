using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using Xunit;

namespace TwitchCraft.Tests.Commands;

public sealed class GameplayCommandIntegrationTests
{
    [Fact]
    public async Task Effect_DeliversValidEffectsAndRejectsInvalidCountsWithoutExtraCharge()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!effect 3");
        await scenario.DispatchAsync("!effect 0");
        await scenario.DispatchAsync("!effect 26");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(97, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(3, commands.Count(command =>
            command.StartsWith("effect give @a[gamemode=!spectator] minecraft:", StringComparison.Ordinal)));
        Assert.Equal(3, commands.Count(command =>
            command.StartsWith("tellraw @a[gamemode=!spectator] ", StringComparison.Ordinal) &&
            command.Contains("viewer gave you", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AllTarget_ExcludesSpectatorsFromCostAndGameplaySelector()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "PlayerTwo", "Spectator"],
            spectators: ["Spectator"],
            multiplayer: true);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!heal all");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(96, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(1, commands.Count(command =>
            string.Equals(command, "effect give @a[gamemode=!spectator] minecraft:instant_health 1 1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NamedTargets_RejectOfflineAndSpectatorPlayersWithoutCharge()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "PlayerTwo", "Spectator"],
            spectators: ["Spectator"],
            multiplayer: true);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!heal MissingPlayer");
        await scenario.DispatchAsync("!heal Spectator");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.False(commands.Exists(command => command.Contains("minecraft:instant_health", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Enchant_RebuildsHeldItemsAndScalesCostAcrossTargets()
    {
        const string heldItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "PlayerTwo"],
            multiplayer: true,
            selectedItem: heldItem);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!enchant all");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        List<string> enchants = commands
            .Where(command =>
                command.StartsWith("item replace entity @a[name=\"", StringComparison.Ordinal) &&
                command.Contains("weapon.mainhand with minecraft:diamond_sword[", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(70, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(2, enchants.Count);
        Assert.All(enchants, enchant => Assert.Contains("minecraft:enchantments=", enchant, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rename_PreservesHeldItemsAndScalesCostAcrossTargets()
    {
        const string heldItem = "{id:'minecraft:diamond_sword',count:2,components:{\"minecraft:damage\":5}}";
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "PlayerTwo"],
            multiplayer: true,
            selectedItem: heldItem);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!rename all");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        List<string> renames = commands
            .Where(command =>
                command.StartsWith("item replace entity @a[name=\"", StringComparison.Ordinal) &&
                command.Contains("weapon.mainhand with minecraft:diamond_sword[", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(85, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(2, renames.Count);
        Assert.All(renames, rename =>
        {
            Assert.Contains("minecraft:damage=5", rename, StringComparison.Ordinal);
            Assert.Contains("minecraft:custom_name=", rename, StringComparison.Ordinal);
            Assert.Contains("Diamond Sword", rename, StringComparison.Ordinal);
            Assert.EndsWith(" 2", rename, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SwitchMilk_UsesSingleScopedTagAndCleansUp()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "PlayerTwo"],
            multiplayer: true);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!switchmilk PlayerTwo");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(94, scenario.Runtime.Tokens.GetBalance("viewer"));
        string first = Assert.Single(
            commands,
            command => command.StartsWith("tag @a remove tc_switchmilk_", StringComparison.Ordinal));
        string tag = first["tag @a remove ".Length..];
        string tagged = "@a[tag=" + tag + "]";
        Assert.True(commands.Exists(command => command.Contains("run tag @s add " + tag, StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => string.Equals(command, "execute as " + tagged + " run clear @s minecraft:milk_bucket 1", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.StartsWith("execute as " + tagged + " run give @s minecraft:", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command =>
            command.StartsWith("execute if entity " + tagged + " run tellraw ", StringComparison.Ordinal) &&
            command.Contains("GOT MILK SWITCHED!", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => string.Equals(command, "execute if entity " + tagged + " run tag " + tagged + " remove " + tag, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Scared_SpawnsTwentyCatsWithWarningTitles()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!scared");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(85, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(20, commands.Count(command => command.Contains("run summon minecraft:cat ", StringComparison.Ordinal)));
        Assert.Equal(1, commands.Count(command =>
            command.StartsWith("title @a[gamemode=!spectator] times ", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("Scaredy Cat!", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("Stop being scared!", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PursuerCommands_SpawnDistinctThreatsAndChargeIndependently()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 200);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!johnny");
        await scenario.DispatchAsync("!chargedcreeper");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(115, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(1, commands.Count(command => command.Contains("summon minecraft:vindicator", StringComparison.Ordinal) &&
            command.Contains("tc_johnny", StringComparison.Ordinal)));
        Assert.Equal(1, commands.Count(command => command.Contains("summon minecraft:creeper", StringComparison.Ordinal) &&
            command.Contains("powered:1b", StringComparison.Ordinal) &&
            command.Contains("tc_charged_creeper", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("Johnny is coming!", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("A charged creeper is coming!", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task HeartCommands_ScaleAcrossTargetsAndShareCooldownAfterSuccess()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "PlayerTwo"],
            multiplayer: true,
            maxHealth: 20, attributes: "[{Name:'generic.max_health',Modifiers:[{Name:'twitchcraft_health',UUID:[I;-1,-1,-1,-1],Amount:2.0d,Operation:0}]}]", minecraftVersion: "1.20.5");
        scenario.Runtime.Tokens.Award("viewer", 300);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!addheart 2 all");
        await scenario.DispatchAsync("!removeheart 1 all");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(150, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(2, commands.Count(command => command.Contains(" twitchcraft_health 4 add_value", StringComparison.Ordinal)));
        Assert.Contains(commands, command => command.Contains(" modifier remove ffffffff-ffff-ffff-ffff-ffffffffffff", StringComparison.Ordinal));
        Assert.False(commands.Exists(command => command.Contains(" -2 add_value", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task HeartCommands_RejectBothHealthLimitsWithoutCharging()
    {
        await using (MinecraftRuntimeScenario upper = await StartAsync(maxHealth: 40))
        {
            upper.Runtime.Tokens.Award("viewer", 100);
            int cursor = upper.CaptureCommandCursor();

            await upper.DispatchAsync("!addheart 1");
            List<string> commands = await upper.DrainCommandsAsync(cursor);

            Assert.Equal(100, upper.Runtime.Tokens.GetBalance("viewer"));
            Assert.False(commands.Exists(command => command.Contains(" modifier add ", StringComparison.Ordinal)));
        }

        await using (MinecraftRuntimeScenario lower = await StartAsync(maxHealth: 10))
        {
            lower.Runtime.Tokens.Award("viewer", 100);
            int cursor = lower.CaptureCommandCursor();

            await lower.DispatchAsync("!removeheart 1");
            List<string> commands = await lower.DrainCommandsAsync(cursor);

            Assert.Equal(100, lower.Runtime.Tokens.GetBalance("viewer"));
            Assert.False(commands.Exists(command => command.Contains(" modifier add ", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task AdministrativeCommands_EnforcePermissionsAndStreamerProtection()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(["PlayerOne", "PlayerTwo"], multiplayer: true);

        int allowedCursor = scenario.CaptureCommandCursor();
        await scenario.DispatchAsync("!ban PlayerTwo griefing hard", "streamer");
        await scenario.DispatchAsync("!kick PlayerTwo testing", "streamer");
        await scenario.DispatchAsync("!unban PlayerTwo", "streamer");
        await scenario.DispatchAsync("!whitelistadd PlayerTwo", "streamer");
        await scenario.DispatchAsync("!whitelistremove PlayerTwo", "streamer");
        List<string> allowed = await scenario.DrainCommandsAsync(allowedCursor);

        Assert.Contains("ban PlayerTwo griefing hard", allowed);
        Assert.Contains("kick PlayerTwo testing", allowed);
        Assert.Contains("pardon PlayerTwo", allowed);
        Assert.Contains("whitelist add PlayerTwo", allowed);
        Assert.Contains("whitelist remove PlayerTwo", allowed);

        int rejectedCursor = scenario.CaptureCommandCursor();
        await scenario.DispatchAsync("!ban PlayerTwo", "viewer");
        await scenario.DispatchAsync("!ban PlayerOne", "streamer");
        await scenario.DispatchAsync("!whitelistremove PlayerOne", "streamer");
        await scenario.DispatchAsync("!kick bad-name", "streamer");
        await scenario.DispatchAsync("!unban bad-name", "streamer");
        await scenario.DispatchAsync("!whitelistadd bad-name", "streamer");
        List<string> rejected = await scenario.DrainCommandsAsync(rejectedCursor);

        Assert.False(rejected.Exists(command => command.StartsWith("ban ", StringComparison.Ordinal)));
        Assert.False(rejected.Exists(command => command.StartsWith("kick ", StringComparison.Ordinal)));
        Assert.False(rejected.Exists(command => command.StartsWith("pardon ", StringComparison.Ordinal)));
        Assert.False(rejected.Exists(command => command.StartsWith("whitelist add ", StringComparison.Ordinal)));
        Assert.False(rejected.Exists(command => command.StartsWith("whitelist remove ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Swarm_SpawnsFiveDistinctMobsAndChargesOnce()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!swarm");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        List<string> summons = commands
            .Where(command => command.StartsWith("execute at @a[gamemode=!spectator] run summon minecraft:", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(55, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(5, summons.Count);
        Assert.Equal(5, summons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Mlg_DispatchesBothDimensionSafeBranchesAndChargesOnce()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 200);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!mlg");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(50, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(5, commands.Count(command => command.Contains("dimension minecraft:the_nether", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("if dimension minecraft:the_nether run fill", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("unless dimension minecraft:the_nether run give @s minecraft:water_bucket 1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Slaughter_RestoresMobLootAfterKillAndChargesOnce()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!slaughter");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        int kill = commands.FindIndex(command =>
            command.Contains("type=!minecraft:player", StringComparison.Ordinal) &&
            command.EndsWith("run kill @s", StringComparison.Ordinal));
        Assert.Equal(70, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.True(kill >= 2 && kill + 3 < commands.Count);
        string gameRule = commands[kill - 1][.." false".Length];
        Assert.Equal("execute store result storage twitchcraft:runtime slaughter_mob_loot byte 1 run " + gameRule, commands[kill - 2]);
        Assert.Equal("execute if data storage twitchcraft:runtime {slaughter_mob_loot:1b} run " + gameRule + " true", commands[kill + 1]);
        Assert.Equal("execute unless data storage twitchcraft:runtime {slaughter_mob_loot:1b} run " + gameRule + " false", commands[kill + 2]);
        Assert.Equal("data remove storage twitchcraft:runtime slaughter_mob_loot", commands[kill + 3]);
    }

    [Fact]
    public async Task RandomGameplayCommands_DispatchValidCommandsAndChargeOnceEach()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!loot");
        await scenario.DispatchAsync("!mob");
        await scenario.DispatchAsync("!weather");
        await scenario.DispatchAsync("!insult");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        int lootCommands = commands.Count(command =>
            command.StartsWith("execute at @a[gamemode=!spectator] run loot spawn ", StringComparison.Ordinal));

        Assert.Equal(70, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.InRange(lootCommands, 3, 4);
        Assert.Equal(1, commands.Count(command =>
            command.StartsWith("execute at @a[gamemode=!spectator] run summon minecraft:", StringComparison.Ordinal)));
        Assert.Equal(1, commands.Count(command =>
            string.Equals(command, "weather rain", StringComparison.Ordinal) ||
            string.Equals(command, "weather thunder", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains("Wow, you suck!", StringComparison.Ordinal)));
    }

    private static Task<MinecraftRuntimeScenario> StartAsync(
        IReadOnlyList<string>? players = null,
        IReadOnlyList<string>? spectators = null,
        bool multiplayer = false,
        double maxHealth = 20,
        string? selectedItem = null, string? attributes = null, string? minecraftVersion = null)
        => MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players,
            spectators,
            multiplayer,
            maxHealth,
            selectedItem, attributes, minecraftVersion);
}
