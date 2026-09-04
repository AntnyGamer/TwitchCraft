using System;
using System.Collections.Generic;
using System.Globalization;

namespace TwitchCraftBot_V1;

public static partial class CommandList
{
    private static readonly string[] InsultTitleColors = ["red", "gold", "yellow", "green", "aqua", "blue", "light_purple", "white"];
    private static readonly string[] EffectLevels = ["I", "II", "III", "IV", "V"];
    private static readonly TimeSpan GambleTokenCooldown = TimeSpan.FromMinutes(5);
    private const string DefaultCommandTextColor = "yellow";

    public static Dictionary<string, ChatCommandHandler> BuildCommands(
        BotMainHandler runtime,
        Dictionary<string, ChatCommandStatisticFlags>? statisticFlags = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return new CommandBuildContext(runtime, statisticFlags).Build();
    }

    private sealed partial class CommandBuildContext
    {
        private const int MaxEffectCount = 25;
        private const ChatCommandStatisticFlags GameCommand = ChatCommandStatisticFlags.GameAffecting;
        private const ChatCommandStatisticFlags DangerousCommand = ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Dangerous;
        private const ChatCommandStatisticFlags NiceCommand = ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Nice;

        private readonly BotMainHandler runtime;
        private Dictionary<string, ChatCommandHandler> handlers;
        private Dictionary<string, ChatCommandStatisticFlags>? statisticFlags;

        internal CommandBuildContext(
            BotMainHandler runtime,
            Dictionary<string, ChatCommandStatisticFlags>? statisticFlags)
        {
            this.runtime = runtime;
            this.statisticFlags = statisticFlags;
            handlers = new Dictionary<string, ChatCommandHandler>(64, StringComparer.OrdinalIgnoreCase);
        }

        internal Dictionary<string, ChatCommandHandler> Build()
        {
            AddCommand("ban", BanAsync);
            AddTokenHandlers(runtime, handlers, SayAsync, SuccessAsync, ConfirmAsync, RequirePermissionAsync);
            AddCommand("commandstats", CommandStatsAsync);
            AddCommand("followreward", FollowRewardAsync);
            AddCommand("help", HelpAsync);
            AddCommand("kick", KickAsync);
            AddCommand("playerlist", PlayerListAsync);
            AddCommand("unban", UnbanAsync);
            AddCommand("whitelistadd", WhitelistAddAsync);
            AddCommand("whitelistremove", WhitelistRemoveAsync);
            AddTargetCommands(
                new("anvil", 5, target =>
                [
                    MinecraftCommandBuilder.ClearColumn(target.Selector, 5),
                    MinecraftCommandBuilder.DropAnvil(target.Selector)
                ], (sender, _) => sender + " dropped an anvil on top of you.", "GOT AN ANVIL DROPPED ON THEM!", (sender, target) => $"{sender}, you dropped an anvil on {TargetName(target)}.", StatisticFlags: DangerousCommand),
                new("clear", 125, target => [$"clear {target.Selector}"], (sender, _) => sender + " cleared your inventory.", "GOT THEIR INVENTORY CLEARED!", (sender, target) => sender + ", you cleared " + TargetName(target) + "'s inventory!", StatisticFlags: DangerousCommand),
                new("clearhand", 25, target => [MinecraftCommandBuilder.ClearMainHand(target.Selector)], (sender, _) => sender + " cleared your hand.", "GOT THEIR HAND CLEARED!", (sender, target) => sender + ", you cleared " + TargetName(target) + "'s hand.", StatisticFlags: DangerousCommand),
                new("explode", 15, target =>
                [
                    MinecraftCommandBuilder.SpawnPrimedTnt(target.Selector),
                    MinecraftCommandBuilder.PlayTntSound(target.Selector)
                ], (sender, _) => sender + " placed TNT on you.", "GOT BOOMED!", (sender, target) => $"{sender}, you placed TNT on {TargetName(target)}.", StatisticFlags: DangerousCommand),
                new("freeze", 30, target => [$"effect give {target.Selector} minecraft:slowness 15 255"], (sender, _) => sender + " froze you for 15 seconds!", "GOT FROZEN!", (sender, target) => $"{sender}, you froze {TargetName(target)}.", StatisticFlags: DangerousCommand),
                new("givelight", 3, target => [$"execute at {target.Selector} run setblock ~ ~1 ~ minecraft:light"], (sender, _) => "Let there be light! (from " + sender + ")", "GOT A LIGHT SOURCE!", (sender, target) => sender + ", you gave " + TargetName(target) + " a source of light.", StatisticFlags: NiceCommand),
                new("heal", 3, target => [MinecraftCommandBuilder.Heal(target.Selector)], (sender, _) => sender + " healed you.", "GOT HEALED!", (sender, target) => $"{sender}, you healed {TargetName(target)}.", StatisticFlags: NiceCommand),
                new("invincible", 15, target => [$"effect give {target.Selector} minecraft:resistance 15 255 true"], (sender, _) => sender + " made you invincible for 15 seconds!", "WAS MADE INVINCIBLE!", (sender, target) => sender + ", you made " + TargetName(target) + " invincible for 15 seconds.", StatisticFlags: NiceCommand),
                new("lava", 15, target => [$"execute at {target.Selector} run setblock ~ ~3 ~ minecraft:lava"], (sender, _) => sender + " released lava above you.", "GOT LAVA RELEASED ON THEM!", (sender, target) => $"{sender}, you released lava above {TargetName(target)}.", StatisticFlags: DangerousCommand),
                new("removeblock", 50, target =>
                [
                    "execute at " + target.Selector +
                    " unless block ~ ~-1 ~ minecraft:bedrock" +
                    " unless block ~ ~-1 ~ minecraft:chest" +
                    " unless block ~ ~-1 ~ minecraft:trapped_chest" +
                    " unless block ~ ~-1 ~ minecraft:ender_chest" +
                    " run setblock ~ ~-1 ~ minecraft:air"
                ], (sender, _) => sender + " removed the block below you.", "GOT THEIR FEET SWEPT!", (sender, target) => sender + ", you removed the block below " + TargetName(target) + ".", StatisticFlags: DangerousCommand),
                new("teleport", 70, target =>
                [
                    "execute as " + target.Selector + " at @s if dimension minecraft:the_nether run spreadplayers ~ ~ 0 2000 under 127 false @s",
                    "execute as " + target.Selector + " at @s unless dimension minecraft:the_nether run spreadplayers ~ ~ 0 2000 false @s"
                ], (sender, _) => sender + " teleported you to a random location.", "GOT RANDOMLY TELEPORTED!", (sender, target) => sender + ", you teleported " + TargetName(target) + " to a random location.", StatisticFlags: DangerousCommand),
                new("turnaround", 5, target => [MinecraftCommandBuilder.TurnAround(target.Selector)], (sender, _) => sender + " turned you around.", "GOT TURNED AROUND!", (sender, target) => sender + ", you turned " + TargetName(target) + " around.", StatisticFlags: DangerousCommand),
                new("totem", 100, target => [$"item replace entity {target.Selector} weapon.offhand with minecraft:totem_of_undying"], (sender, _) => sender + " gave you a Totem of Undying!", "GOT A TOTEM!", (sender, target) => sender + ", you gave " + TargetName(target) + " a Totem of Undying.", StatisticFlags: NiceCommand),
                new("troll", 5, target => [$"execute as {target.Selector} at @s run playsound minecraft:entity.creeper.primed master @s ~ ~ ~ 1 1"], (_, _) => null, null, (sender, target) => sender + ", you played a creeper noise on " + TargetName(target) + ".", StatisticFlags: DangerousCommand),
                new("water", 15, target => [$"execute at {target.Selector} run setblock ~ ~3 ~ minecraft:water"], (sender, _) => sender + " released water above you.", "GOT WATER RELEASED ON THEM!", (sender, target) => $"{sender}, you released water above {TargetName(target)}.", StatisticFlags: GameCommand),
                new("xp", 5, target => [$"experience add {target.Selector} -1 levels"], (sender, _) => sender + " took away 1 of your XP levels.", "LOST 1 XP LEVEL!", (sender, target) => sender + ", you removed 1 XP level from " + TargetName(target) + ".", StatisticFlags: DangerousCommand));
            AddCommand("effect", EffectAsync, GameCommand);
            AddCommand("enchant", EnchantAsync, NiceCommand);
            AddTargetCommand("chargedcreeper", ChargedCreeperAsync, DangerousCommand, minimumTokenCost: 45);
            AddTargetCommand("fireworks", FireworksAsync, GameCommand, minimumTokenCost: 10);
            AddTargetCommand("giant", (target, sender, ct) => TimedScaleAsync(target, sender, "giant", 2.0, "giant-sized", "BECAME GIANT-SIZED!", ct), DangerousCommand, minimumTokenCost: 20);
            AddTargetCommand("insult", InsultAsync, DangerousCommand, minimumTokenCost: 5);
            AddTargetCommand("johnny", JohnnyAsync, DangerousCommand, minimumTokenCost: 40);
            AddCommand("lightning", LightningAsync, DangerousCommand);
            AddTargetCommand("loot", LootAsync, NiceCommand, minimumTokenCost: 5);
            AddTargetCommand("mlg", MlgAsync, DangerousCommand, minimumTokenCost: 150);
            AddTargetCommand("mob", MobAsync, DangerousCommand, minimumTokenCost: 10);
            AddCommand("night", NightAsync, DangerousCommand);
            AddCommand("rename", RenameAsync, GameCommand);
            AddTargetCommand("scared", ScaredAsync, DangerousCommand, minimumTokenCost: 15);
            AddTargetCommand("slaughter", SlaughterAsync, DangerousCommand, minimumTokenCost: 30);
            AddTargetCommand("swarm", SwarmAsync, DangerousCommand, minimumTokenCost: 45);
            AddTargetCommand("switchmilk", SwitchMilkAsync, DangerousCommand, minimumTokenCost: 6);
            AddTargetCommand("tiny", (target, sender, ct) => TimedScaleAsync(target, sender, "tiny", 0.5, "tiny", "BECAME TINY!", ct), DangerousCommand, minimumTokenCost: 20);
            AddCommand("weather", WeatherAsync, DangerousCommand);
            MinigameManager.AddMinigameHandlers(runtime, handlers, SayAsync, SuccessAsync);

            Dictionary<string, ChatCommandHandler> result = handlers;
            handlers = null!;
            statisticFlags = null;
            return result;
        }
    }

    private sealed record TargetedCommandDefinition(
        string Name,
        int BaseCost,
        Func<ResolvedTarget, IEnumerable<string>> BuildCommands,
        Func<string, ResolvedTarget, string?> BuildTargetMessage,
        string? OthersMessage,
        Func<string, ResolvedTarget, string> BuildChannelMessage,
        string Color = "yellow",
        bool Bold = true,
        string? OthersColor = null,
        ChatCommandStatisticFlags StatisticFlags = ChatCommandStatisticFlags.GameAffecting);

    private static string NormalizeUser(string? value) => CommandUserHelper.NormalizeUser(value);
    private static string TokenLabel(int amount) => amount == 1 ? "token" : "tokens";
    private static string PrettyName(string id)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((id ?? string.Empty).Replace('_', ' '));
}
