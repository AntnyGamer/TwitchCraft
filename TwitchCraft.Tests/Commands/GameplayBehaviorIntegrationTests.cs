using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using Xunit;

namespace TwitchCraft.Tests.Commands;

public sealed class GameplayBehaviorIntegrationTests
{
    private const string PlayerSelector = "@a[name=\"PlayerOne\",gamemode=!spectator]";

    [Fact]
    public async Task EffectCommand_MultipleEffectsChargesPerEffectAndDeliversEach()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!effect 3");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(97, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(3, commands.Count(command =>
            command.StartsWith("effect give @a[gamemode=!spectator] minecraft:", StringComparison.Ordinal)));
        Assert.Equal(3, commands.Count(command =>
            command.StartsWith("tellraw @a[gamemode=!spectator] ", StringComparison.Ordinal) &&
            command.Contains("viewer gave you", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EffectCommand_InvalidCountsDoNotChargeOrReachMinecraft()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!effect 0");
        await scenario.DispatchAsync("!effect 26");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.False(commands.Exists(command => command.StartsWith("effect give ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AllTarget_ChargesForEveryActivePlayerAndUsesNonSpectatorSelector()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(["PlayerOne", "PlayerTwo"], multiplayer: true);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!heal all");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(96, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(1, commands.Count(command =>
            string.Equals(command, "effect give @a[gamemode=!spectator] minecraft:instant_health 1 1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task OfflineTarget_IsRejectedWithoutChargeOrGameplayCommand()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(["PlayerOne", "PlayerTwo"], multiplayer: true);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!heal MissingPlayer");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.False(commands.Exists(command => command.Contains("minecraft:instant_health", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SpectatorTarget_IsRejectedWithoutChargeOrGameplayCommand()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(
            ["PlayerOne", "Spectator"],
            spectators: ["Spectator"],
            multiplayer: true);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!heal Spectator");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.False(commands.Exists(command => command.Contains("minecraft:instant_health", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EnchantCommand_RebuildsHeldItemAndChargesOnlyAfterDelivery()
    {
        const string heldItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await StartAsync(selectedItem: heldItem);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!enchant");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(80, scenario.Runtime.Tokens.GetBalance("viewer"));
        string enchant = Assert.Single(
            commands,
            command => command.StartsWith(
                "item replace entity " + PlayerSelector + " weapon.mainhand with minecraft:diamond_sword[",
                StringComparison.Ordinal));
        Assert.Contains("minecraft:enchantments=", enchant, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameCommand_PreservesHeldItemAndAppliesRedeemerName()
    {
        const string heldItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await StartAsync(selectedItem: heldItem);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!rename");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(90, scenario.Runtime.Tokens.GetBalance("viewer"));
        string rename = Assert.Single(
            commands,
            command => command.StartsWith(
                "item replace entity " + PlayerSelector + " weapon.mainhand with minecraft:diamond_sword[",
                StringComparison.Ordinal));
        Assert.Contains("minecraft:custom_name=", rename, StringComparison.Ordinal);
        Assert.Contains("Diamond Sword", rename, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchMilkCommand_UsesOneScopedTagAndAlwaysCleansItUp()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync();
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!switchmilk");
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
        Assert.True(commands.Exists(command => string.Equals(command, "execute if entity " + tagged + " run tag " + tagged + " remove " + tag, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ScaredCommand_SpawnsExactlyTwentyCatsWithWarningTitles()
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
    public async Task PursuerCommands_SpawnTheirDistinctThreatsAndChargeIndependently()
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
    public async Task HeartCommands_ShareCooldownAfterASuccessfulHealthChange()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(maxHealth: 20);
        scenario.Runtime.Tokens.Award("viewer", 200);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!addheart 2");
        await scenario.DispatchAsync("!removeheart 1");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.Equal(1, commands.Count(command => command.Contains(" modifier add twitchcraft:heart_", StringComparison.Ordinal)));
        Assert.True(commands.Exists(command => command.Contains(" 4 add_value", StringComparison.Ordinal)));
        Assert.False(commands.Exists(command => command.Contains(" -2 add_value", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AddHeartCommand_RejectsHealthAboveTwentyHeartsWithoutCharging()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(maxHealth: 40);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!addheart 1");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.False(commands.Exists(command => command.Contains(" modifier add ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RemoveHeartCommand_RejectsHealthBelowFiveHeartsWithoutCharging()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(maxHealth: 10);
        scenario.Runtime.Tokens.Award("viewer", 100);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!removeheart 1");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Equal(100, scenario.Runtime.Tokens.GetBalance("viewer"));
        Assert.False(commands.Exists(command => command.Contains(" modifier add ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdministrativeCommands_StreamOwnerCanModerateAndManageWhitelist()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(["PlayerOne", "PlayerTwo"], multiplayer: true);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!ban PlayerTwo griefing hard", "streamer");
        await scenario.DispatchAsync("!kick PlayerTwo testing", "streamer");
        await scenario.DispatchAsync("!unban PlayerTwo", "streamer");
        await scenario.DispatchAsync("!whitelistadd PlayerTwo", "streamer");
        await scenario.DispatchAsync("!whitelistremove PlayerTwo", "streamer");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.Contains("ban PlayerTwo griefing hard", commands);
        Assert.Contains("kick PlayerTwo testing", commands);
        Assert.Contains("pardon PlayerTwo", commands);
        Assert.Contains("whitelist add PlayerTwo", commands);
        Assert.Contains("whitelist remove PlayerTwo", commands);
    }

    [Fact]
    public async Task AdministrativeCommands_RejectUnauthorizedViewerAndProtectStreamerAccount()
    {
        await using MinecraftRuntimeScenario scenario = await StartAsync(["PlayerOne", "PlayerTwo"], multiplayer: true);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.DispatchAsync("!ban PlayerTwo", "viewer");
        await scenario.DispatchAsync("!ban PlayerOne", "streamer");
        await scenario.DispatchAsync("!whitelistremove PlayerOne", "streamer");
        List<string> commands = await scenario.DrainCommandsAsync(cursor);

        Assert.False(commands.Exists(command => command.StartsWith("ban ", StringComparison.Ordinal)));
        Assert.False(commands.Exists(command => command.StartsWith("whitelist remove ", StringComparison.Ordinal)));
    }

    private static Task<MinecraftRuntimeScenario> StartAsync(
        IReadOnlyList<string>? players = null,
        IReadOnlyList<string>? spectators = null,
        bool multiplayer = false,
        double maxHealth = 20,
        string? selectedItem = null)
        => MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players,
            spectators,
            multiplayer,
            maxHealth,
            selectedItem);
}
