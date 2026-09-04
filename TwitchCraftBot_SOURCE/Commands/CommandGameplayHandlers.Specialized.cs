using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class CommandList
{
    private sealed partial class CommandBuildContext
    {
        private static readonly TimeSpan PlayerScaleDuration = TimeSpan.FromSeconds(30);

        async Task ChargedCreeperAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            List<string> commands = MinecraftCommandFeatureBuilder.BuildChargedCreeper(
                target.Selector,
                BotMainHandler.Randomizer,
                runtime.UsesInlineTextComponentSyntax,
                runtime.UsesModernEntityAttributeNbt);
            commands.Add(MinecraftCommandBuilder.TitleTimes(target.Selector, 0, 100, 10));
            commands.Add(MinecraftCommandBuilder.Title(target.Selector, " ", "white", runtime.UsesInlineTextComponentSyntax));
            commands.Add(MinecraftCommandBuilder.Subtitle(target.Selector, "Charged Creeper is coming!", "red", runtime.UsesInlineTextComponentSyntax));
            await SendPricedReplyAsync(
                target,
                sender,
                45,
                _ => commands,
                sender + " sent a charged creeper after you.",
                "A CHARGED CREEPER IS COMING!",
                sender + ", you spawned a charged creeper for " + TargetName(target) + ".",
                "yellow",
                true,
                "red",
                ct).ConfigureAwait(false);
        }

        async Task FireworksAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string fireworkCommand = "execute at " + target.Selector + " run summon firework_rocket ~ ~1 ~ {LifeTime:20}";
            if (!await SendPricedAsync(target, sender, 10, fireworkCommand, ct, sender + " sent you some fireworks.", "GOT FIREWORKS!").ConfigureAwait(false))
                return;
            await ConfirmAsync(sender + ", you sent " + TargetName(target) + " some fireworks.", ct).ConfigureAwait(false);
            if (!runtime.Commands.TryStartFireworks())
                return;
            Task fireworksRepeatTask = Task.Run(async () =>
            {
                try
                {
                    for (int k = 1; k < 10; k++)
                    {
                        if (ct.IsCancellationRequested)
                            break;
                        await runtime.SendServerCommandAsync(fireworkCommand, ct).ConfigureAwait(false);
                        await Task.Delay(150, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    runtime.AddServerLogLine(ErrorHandling.FormatLog("Fireworks repeat failed", ex));
                }
                finally
                {
                    runtime.Commands.StopFireworks();
                }
            }, CancellationToken.None);
            runtime.TrackTask(fireworksRepeatTask);
        }
        async Task InsultAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string color = InsultTitleColors[BotMainHandler.Randomizer.Next(InsultTitleColors.Length)];
            await SendPricedReplyAsync(
                target,
                sender,
                5,
                _ =>
                [
                    MinecraftCommandBuilder.TitleTimes(target.Selector, 0, 400, 10),
                    MinecraftCommandBuilder.Title(target.Selector, "Wow, you suck!", color, runtime.UsesInlineTextComponentSyntax)
                ],
                sender + " insulted you!",
                "GOT INSULTED!",
                sender + ", you insulted " + TargetName(target) + "...",
                "yellow",
                true,
                null,
                ct);
        }
        async Task JohnnyAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            List<string> commands = MinecraftCommandFeatureBuilder.BuildJohnny(target.Selector, BotMainHandler.Randomizer, runtime.UsesInlineTextComponentSyntax, runtime.UsesModernEntityAttributeNbt);
            commands.Add(MinecraftCommandBuilder.TitleTimes(target.Selector, 0, 100, 10));
            commands.Add(MinecraftCommandBuilder.Title(target.Selector, " ", "white", runtime.UsesInlineTextComponentSyntax));
            commands.Add(MinecraftCommandBuilder.Subtitle(target.Selector, "Johnny is coming!", "red", runtime.UsesInlineTextComponentSyntax));
            await SendPricedReplyAsync(
                target,
                sender,
                40,
                _ => commands,
                sender + " sent Johnny after you.",
                "JOHNNY IS COMING!",
                sender + ", you spawned Johnny for " + TargetName(target) + ".",
                "yellow",
                true,
                "red",
                ct);
        }
        async Task LightningAsync(string[]? args, string sender, CancellationToken ct)
        {
            if (!await RequireMinecraftAsync(sender, ct).ConfigureAwait(false))
                return;
            if (!runtime.Commands.TryUseLightning(out TimeSpan remaining, out DateTime lightningReservationUtc))
            {
                await SayAsync(sender + ", command is on global cooldown. Try again in " + runtime.FormatCooldown(remaining) + ".", ct).ConfigureAwait(false);
                return;
            }
            ResolvedTarget? target;
            try
            {
                target = await PrepareTargetAsync(args, sender, ct, checkGameCooldown: false, minimumTokenCost: 50).ConfigureAwait(false);
            }
            catch
            {
                runtime.Commands.ClearLightningCooldown(lightningReservationUtc);
                throw;
            }
            if (target == null)
            {
                runtime.Commands.ClearLightningCooldown(lightningReservationUtc);
                return;
            }
            int cost = runtime.Commands.ScaleCost(50, target.PlayerCount);
            if (!await TrySendPaidNoCooldownAsync(
                    sender,
                    cost,
                    MinecraftCommandBuilder.Lightning(target.Selector),
                    ct,
                    () => runtime.Commands.ClearLightningCooldown(lightningReservationUtc)).ConfigureAwait(false))
            {
                return;
            }
            await runtime.SendTellrawAsync(target.Selector, sender + " struck you with lightning!", "yellow", true, ct).ConfigureAwait(false);
            await NotifyOthersAsync(target, "GOT STRUCK BY LIGHTNING!", "yellow", true, ct).ConfigureAwait(false);
            await ConfirmAsync(sender + ", you struck " + TargetName(target) + " with lightning.", ct).ConfigureAwait(false);
        }
        async Task LootAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            int times = BotMainHandler.Randomizer.Next(3, 5);
            var commands = new List<string>(times);
            for (int i = 0; i < times; i++)
            {
                double offsetX = (BotMainHandler.Randomizer.NextDouble() * 2.0) - 1.0;
                double offsetZ = (BotMainHandler.Randomizer.NextDouble() * 2.0) - 1.0;
                commands.Add(MinecraftCommandBuilder.Loot(target.Selector, runtime.GetRandomLootTable(), offsetX, offsetZ));
            }
            await SendPricedReplyAsync(
                target,
                sender,
                5,
                _ => commands,
                sender + " gave you a pile of loot.",
                "GOT SOME LOOT!",
                sender + ", you gave " + TargetName(target) + " a pile of loot.",
                "yellow",
                true,
                null,
                ct);
        }
        async Task MobAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string mob = runtime.GetRandomMob();
            string pretty = PrettyName(mob);
            await SendPricedReplyAsync(
                target,
                sender,
                10,
                MinecraftCommandBuilder.SummonMob(target.Selector, mob),
                sender + " summoned a " + pretty + " on you.",
                "GOT A MOB SPAWNED ON THEM!",
                sender + ", you summoned a " + pretty + " on " + TargetName(target) + ".",
                "yellow",
                true,
                null,
                ct);
        }
        async Task NightAsync(string[]? _, string sender, CancellationToken ct)
        {
            if (!await TrySendPricedAsync(sender, 15, "time set night", ct).ConfigureAwait(false))
                return;
            await runtime.SendTellrawAsync("@a", sender + " made it night.", "yellow", true, ct).ConfigureAwait(false);
            await ConfirmAsync(sender + ", you changed the time to night.", ct).ConfigureAwait(false);
        }
        async Task EnchantAsync(string[]? args, string sender, CancellationToken ct)
        {
            const int baseCost = 20;
            ResolvedTarget? target = await PrepareTargetAsync(args, sender, ct, minimumTokenCost: baseCost).ConfigureAwait(false);
            if (target == null)
                return;

            bool targetsEveryone = IsEveryone(target);
            List<string> playerNames = await GetPlayersAsync(target, ct).ConfigureAwait(false);
            if (playerNames.Count == 0)
            {
                await SayAsync(sender + ", that player could not be resolved for !enchant.", ct).ConfigureAwait(false);
                return;
            }

            Dictionary<string, string?>? selectedItemsByPlayer = playerNames.Count > 1
                ? await runtime.QueryItemsAsync(playerNames, ct).ConfigureAwait(false)
                : null;
            List<string> enchantCommands = new(playerNames.Count);
            List<(string Player, string Item, string Enchant, int Level, bool HadItem)> rolls = new(playerNames.Count);

            foreach (string playerName in playerNames)
            {
                string? selectedItemData;
                if (selectedItemsByPlayer != null)
                    selectedItemsByPlayer.TryGetValue(playerName, out selectedItemData);
                else
                    selectedItemData = await runtime.QueryItemAsync(playerName, ct).ConfigureAwait(false);

                string singleSelector = MinecraftCommandBuilder.PlayerSelector(playerName);
                MinecraftItemEnchantHelper.PickEnchant(
                    BotMainHandler.Randomizer,
                    runtime.SupportsMaceEnchantments,
                    out string enchantID,
                    out string prettyEnchantName,
                    out int level);
                string enchantCommand = string.Empty;
                string prettyItemName = string.Empty;
                bool hadItem = !string.IsNullOrWhiteSpace(selectedItemData) &&
                    MinecraftItemRenameHelper.TryBuildEnchantCommand(
                        singleSelector,
                        selectedItemData,
                        enchantID,
                        level,
                        runtime.UsesFlattenedEnchantmentsComponent,
                        out enchantCommand,
                        out prettyItemName);
                if (!hadItem)
                {
                    prettyItemName = string.Empty;
                    enchantCommand = MinecraftItemEnchantHelper.BuildEnchant(singleSelector, enchantID, level);
                }

                enchantCommands.Add(enchantCommand);
                rolls.Add((playerName, prettyItemName, prettyEnchantName, level, hadItem));
            }

            int cost = runtime.Commands.ScaleCost(baseCost, playerNames.Count);
            if (!await TrySendPricedAsync(sender, cost, () => enchantCommands, ct).ConfigureAwait(false))
                return;

            foreach ((string playerName, string item, string enchant, int enchantLevel, bool hadItem) in rolls)
            {
                string levelText = FormatLevel(enchantLevel);
                string notification = hadItem
                    ? sender + " enchanted your held " + item + " with " + enchant + " " + levelText + "."
                    : sender + " rolled " + enchant + " " + levelText + " for you, but you were not holding an item.";
                await runtime.SendTellrawAsync(
                    MinecraftCommandBuilder.PlayerSelector(playerName),
                    notification,
                    DefaultCommandTextColor,
                    true,
                    ct).ConfigureAwait(false);
            }

            if (rolls.Count == 1)
            {
                (string playerName, string item, string enchant, int enchantLevel, bool hadItem) = rolls[0];
                string result = hadItem
                    ? "you enchanted " + playerName + "'s held " + item + " with " + enchant + " " + FormatLevel(enchantLevel) + "."
                    : "you rolled " + enchant + " " + FormatLevel(enchantLevel) + " for " + playerName + ", but they were not holding an item.";
                await ConfirmAsync(
                    sender + ", " + result,
                    ct).ConfigureAwait(false);
            }
            else if (targetsEveryone)
            {
                await ConfirmAsync(sender + ", you rolled random enchantments for " + rolls.Count.ToString(CultureInfo.InvariantCulture) + " players.", ct).ConfigureAwait(false);
            }
            else
            {
                await ConfirmAsync(sender + ", you rolled random enchantments for " + rolls.Count.ToString(CultureInfo.InvariantCulture) + " targets in " + TargetName(target) + ".", ct).ConfigureAwait(false);
            }
        }

        async Task TimedScaleAsync(
            ResolvedTarget target,
            string sender,
            string commandName,
            double scale,
            string sizeDescription,
            string othersMessage,
            CancellationToken ct)
        {
            const int baseCost = 20;
            List<string> playerNames = await GetPlayersAsync(target, ct).ConfigureAwait(false);
            if (playerNames.Count == 0)
            {
                await SayAsync(sender + ", that player could not be resolved for this size command.", ct).ConfigureAwait(false);
                return;
            }

            if (!runtime.Commands.TryUseScaleCommand(commandName, out TimeSpan remaining, out DateTime cooldownReservationUtc))
            {
                await SayAsync(sender + ", command is on global cooldown. Try again in " + runtime.FormatCooldown(remaining) + ".", ct).ConfigureAwait(false);
                return;
            }

            int cost = runtime.Commands.ScaleCost(baseCost, playerNames.Count);
            bool sent;
            try
            {
                sent = await runtime.ApplyTimedScaleAsync(
                    playerNames,
                    scale,
                    PlayerScaleDuration,
                    (commands, token) => TrySendPricedAsync(sender, cost, () => commands, token),
                    ct).ConfigureAwait(false);
            }
            catch
            {
                runtime.Commands.ClearScaleCooldown(commandName, cooldownReservationUtc);
                throw;
            }

            if (!sent)
            {
                runtime.Commands.ClearScaleCooldown(commandName, cooldownReservationUtc);
                return;
            }

            await runtime.SendTellrawAsync(
                target.Selector,
                sender + " made you " + sizeDescription + " for 30 seconds!",
                "yellow",
                true,
                ct).ConfigureAwait(false);
            await NotifyOthersAsync(target, othersMessage, "yellow", true, ct).ConfigureAwait(false);
            await ConfirmAsync(
                sender + ", you made " + TargetName(target) + " " + sizeDescription + " for 30 seconds.",
                ct).ConfigureAwait(false);
        }

        async Task<List<string>> GetPlayersAsync(ResolvedTarget target, CancellationToken ct)
        {
            if (IsEveryone(target) || target.PlayerCount > 1)
            {
                return NormalizeTargets(
                    target.TargetablePlayers ?? await runtime.GetPlayersAsync(ct).ConfigureAwait(false));
            }

            string playerName = GetPlayerName(target);
            return playerName.Length > 0 ? [playerName] : [];
        }
        async Task RenameAsync(string[]? args, string sender, CancellationToken ct)
        {
            ResolvedTarget? target = await PrepareTargetAsync(args, sender, ct, minimumTokenCost: 10).ConfigureAwait(false);
            if (target == null)
                return;
            bool targetsEveryone = IsEveryone(target);
            List<string> playerNames;
            if (targetsEveryone || target.PlayerCount > 1)
            {
                playerNames = NormalizeTargets(target.TargetablePlayers ?? await runtime.GetPlayersAsync(ct).ConfigureAwait(false));
            }
            else
            {
                string playerName = GetPlayerName(target);
                if (playerName.Length == 0)
                {
                    await SayAsync(sender + ", that player could not be resolved for !rename.", ct).ConfigureAwait(false);
                    return;
                }
                playerNames = [playerName];
            }
            List<string> renameCommands = new(playerNames.Count);
            List<string> renamedPlayers = new(playerNames.Count);
            string prettyItemName = string.Empty;
            Dictionary<string, string?>? selectedItemsByPlayer = playerNames.Count > 1
                ? await runtime.QueryItemsAsync(playerNames, ct).ConfigureAwait(false)
                : null;
            foreach (string playerName in playerNames)
            {
                string? selectedItemData;
                if (selectedItemsByPlayer != null)
                    selectedItemsByPlayer.TryGetValue(playerName, out selectedItemData);
                else
                    selectedItemData = await runtime.QueryItemAsync(playerName, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(selectedItemData))
                    continue;
                string singleSelector = MinecraftCommandBuilder.PlayerSelector(playerName);
                if (!MinecraftItemRenameHelper.TryBuildRenameCommand(singleSelector, selectedItemData, sender, runtime.UsesInlineTextComponentSyntax, out string renameCommand, out string currentPrettyItemName))
                    continue;
                renameCommands.Add(renameCommand);
                renamedPlayers.Add(playerName);
                if (string.IsNullOrWhiteSpace(prettyItemName))
                    prettyItemName = currentPrettyItemName;
            }
            if (renameCommands.Count == 0)
            {
                await SayAsync(sender + ", " + TargetName(target) + " is not holding a renameable item right now.", ct).ConfigureAwait(false);
                return;
            }
            int cost = runtime.Commands.ScaleCost(10, renameCommands.Count);
            if (!await TrySendPricedAsync(sender, cost, () => renameCommands, ct).ConfigureAwait(false))
                return;
            string notificationMessage = sender + " renamed your held item.";
            if (runtime.RemoteControlEnabled || renamedPlayers.Count == 1)
            {
                foreach (string playerName in renamedPlayers)
                    await runtime.SendTellrawAsync(MinecraftCommandBuilder.PlayerSelector(playerName), notificationMessage, "yellow", true, ct).ConfigureAwait(false);
            }
            else
            {
                List<string> notifyCommands = new(renamedPlayers.Count);
                foreach (string playerName in renamedPlayers)
                    notifyCommands.Add(MinecraftCommandBuilder.Tellraw(MinecraftCommandBuilder.PlayerSelector(playerName), notificationMessage, "yellow", true, runtime.UsesInlineTextComponentSyntax));

                await runtime.SendServerCommandsAsync(notifyCommands, ct).ConfigureAwait(false);
            }
            if (renamedPlayers.Count == 1)
                await ConfirmAsync(sender + ", you renamed " + renamedPlayers[0] + "'s held " + prettyItemName + ".", ct).ConfigureAwait(false);
            else if (targetsEveryone)
                await ConfirmAsync(sender + ", you renamed " + renamedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " players' held items.", ct).ConfigureAwait(false);
            else
                await ConfirmAsync(sender + ", you renamed " + renamedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " held items for " + TargetName(target) + ".", ct).ConfigureAwait(false);
        }
        async Task SwarmAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prettyNames = new List<string>(5);
            var swarmCommands = new List<string>(10);
            while (prettyNames.Count < 5)
            {
                string mob = runtime.GetRandomMob();
                if (!used.Add(mob))
                    continue;
                string pretty = PrettyName(mob);
                prettyNames.Add(pretty);
                swarmCommands.Add(MinecraftCommandBuilder.SummonMob(target.Selector, mob));
                swarmCommands.Add(MinecraftCommandBuilder.Tellraw(target.Selector, sender + " spawned a " + pretty + " on you.", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            }
            await SendPricedReplyAsync(
                target,
                sender,
                45,
                _ => swarmCommands,
                null,
                "GOT SWARMED!",
                sender + ", you spawned " + string.Join(", ", prettyNames) + " on " + TargetName(target) + ".",
                "yellow",
                true,
                null,
                ct);
        }
        async Task SwitchMilkAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            (string itemID, string itemName) = BotMainHandler.Randomizer.Next(100) switch
            {
                < 50 => ("minecraft:bucket", "an empty bucket"),
                < 75 => ("minecraft:water_bucket", "a water bucket"),
                _ => ("minecraft:lava_bucket", "a lava bucket")
            };
            string singleMilkTargetName = GetPlayerName(target);
            if (target.PlayerCount == 1 && !IsEveryone(target) && singleMilkTargetName.Length == 0)
            {
                await SayAsync(sender + ", that player could not be resolved for !switchmilk.", ct).ConfigureAwait(false);
                return;
            }
            string switchMilkTag = runtime.Commands.NextSwitchMilkTag();
            string taggedMilkSelector = "@a[tag=" + switchMilkTag + "]";
            List<string> switchMilkCommands = ["tag @a remove " + switchMilkTag];
            switchMilkCommands.Add("execute as " + target.Selector + " if data entity @s Inventory[{id:\"minecraft:milk_bucket\"}] run tag @s add " + switchMilkTag);
            if (runtime.MultiTargetingEnabled && target.PlayerCount == 1 && !IsEveryone(target) && runtime.HasOtherPlayer(singleMilkTargetName))
            {
                switchMilkCommands.Add(
                    "execute if entity " + taggedMilkSelector +
                    " run " + MinecraftCommandBuilder.Tellraw(MinecraftCommandBuilder.EveryoneExceptSelector(singleMilkTargetName), ((target.DisplayName ?? singleMilkTargetName).ToUpperInvariant()) + " GOT MILK SWITCHED!", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            }
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run clear @s minecraft:milk_bucket 1");
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run give @s " + itemID + " 1");
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run " + MinecraftCommandBuilder.Tellraw("@s", sender + " transformed one of your milk buckets into " + itemName + "!", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            switchMilkCommands.Add("execute if entity " + taggedMilkSelector + " run tag " + taggedMilkSelector + " remove " + switchMilkTag);
            await SendPricedReplyAsync(
                target,
                sender,
                6,
                _ => switchMilkCommands,
                null,
                null,
                sender + ", you changed " + TargetName(target) + "'s milk bucket into " + itemName + " (if they had one).",
                "yellow",
                true,
                null,
                ct);
        }
        async Task WeatherAsync(string[]? _, string sender, CancellationToken ct)
        {
            bool thunder = BotMainHandler.Randomizer.Next(2) == 0;
            string weatherCommand = thunder ? "weather thunder" : "weather rain";
            if (!await TrySendPricedAsync(sender, 10, weatherCommand, ct).ConfigureAwait(false))
                return;
            string weatherAction = thunder ? "started a thunderstorm" : "made it rain";
            await runtime.SendTellrawAsync("@a", sender + " " + weatherAction + ".", "yellow", true, ct).ConfigureAwait(false);
            await ConfirmAsync(sender + ", you " + weatherAction + ".", ct).ConfigureAwait(false);
        }
        Task MlgAsync(ResolvedTarget target, string sender, CancellationToken ct)
            => SendPricedReplyAsync(
                target,
                sender,
                150,
                _ =>
                [
                    "execute as " + target.Selector + " at @s if dimension minecraft:the_nether run fill ~-1 ~ ~-1 ~1 ~50 ~1 air",
                    "execute as " + target.Selector + " at @s if dimension minecraft:the_nether run tp @s ~ ~50 ~",
                    "execute as " + target.Selector + " at @s if dimension minecraft:the_nether run give @s minecraft:cobweb 1",
                    "execute as " + target.Selector + " at @s unless dimension minecraft:the_nether run tp @s ~ ~200 ~",
                    "execute as " + target.Selector + " at @s unless dimension minecraft:the_nether run give @s minecraft:water_bucket 1"
                ],
                sender + " sent you into the sky!",
                "GOT SENT INTO THE SKY!",
                sender + ", you sent " + TargetName(target) + " into the sky.",
                "yellow",
                true,
                null,
                ct);
        Task ScaredAsync(ResolvedTarget target, string sender, CancellationToken ct)
            => SendPricedReplyAsync(
                target,
                sender,
                15,
                _ => MinecraftCommandFeatureBuilder.BuildScared(target.Selector, BotMainHandler.Randomizer, runtime.UsesInlineTextComponentSyntax),
                sender + " thinks you're a scaredy cat and spawned cats above you.",
                "GOT BURIED IN CATS!",
                sender + ", you spawned 20 cats on " + TargetName(target) + ".",
                "yellow",
                true,
                null,
                ct);
        Task SlaughterAsync(ResolvedTarget target, string sender, CancellationToken ct)
            => SendPricedReplyAsync(
                target,
                sender,
                30,
                _ => MinecraftCommandFeatureBuilder.BuildSlaughter(target.Selector, runtime.MobLootGameRuleName),
                sender + " slaughtered any nearby mobs.",
                "GOT THEIR AREA SLAUGHTERED!",
                sender + ", you slaughtered any nearby mobs around " + TargetName(target) + ".",
                "yellow",
                true,
                null,
                ct);

        static string FormatLevel(int level)
            => level switch
            {
                1 => "I",
                2 => "II",
                3 => "III",
                4 => "IV",
                5 => "V",
                _ => Math.Max(1, level).ToString(CultureInfo.InvariantCulture)
            };
    }
}
