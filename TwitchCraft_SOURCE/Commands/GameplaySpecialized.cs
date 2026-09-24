using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public static partial class CommandList
{
    private sealed partial class CommandBuildContext
    {
        private static readonly TimeSpan PlayerScaleDuration = TimeSpan.FromSeconds(30);

        Task ChargedCreeperAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string[] commands = GameplayCommands.BuildChargedCreeper(
                target.Selector,
                Random.Shared,
                runtime.UsesInlineTextComponentSyntax,
                runtime.UsesModernEntityAttributeNbt);
            return SendPricedReplyAsync(
                target,
                sender,
                45,
                _ => commands,
                sender + " summoned a charged creeper on you.",
                "GOT A CHARGED CREEPER SPAWNED ON THEM!",
                sender + ", you summoned a charged creeper on " + TargetName(target) + ".",
                "yellow",
                true,
                "red",
                ct);
        }

        async Task FireworksAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string fireworkCommand = "execute at " + target.Selector + " run summon firework_rocket ~ ~1 ~ {LifeTime:20}";
            if (!runtime.Commands.TryStartFireworks())
            {
                await SayAsync(sender + ", fireworks are already running. Try again in a moment.", ct).ConfigureAwait(false);
                return;
            }

            bool repeatStarted = false;
            try
            {
                if (!await SendPricedAsync(target, sender, 10, fireworkCommand, ct, sender + " sent you some fireworks.", "GOT FIREWORKS!").ConfigureAwait(false))
                    return;
                await ConfirmAsync(sender + ", you sent " + TargetName(target) + " some fireworks.", ct).ConfigureAwait(false);
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
                repeatStarted = true;
                runtime.TrackTask(fireworksRepeatTask);
            }
            finally
            {
                if (!repeatStarted) runtime.Commands.StopFireworks();
            }
        }
        Task InsultAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string color = TitleColors[Random.Shared.Next(TitleColors.Length)];
            return SendPricedReplyAsync(
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
        Task JohnnyAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string[] commands = GameplayCommands.BuildJohnny(target.Selector, Random.Shared, runtime.UsesInlineTextComponentSyntax, runtime.UsesModernEntityAttributeNbt);
            return SendPricedReplyAsync(
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
            if (!runtime.Commands.TryUseTimedCommand("lightning", out TimeSpan remaining, out long reservation))
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
                runtime.Commands.ClearTimedCommandCooldown("lightning", reservation);
                throw;
            }
            if (target == null)
            {
                runtime.Commands.ClearTimedCommandCooldown("lightning", reservation);
                return;
            }
            int cost = runtime.Commands.ScaleCost(50, target.PlayerCount);
            if (!await TrySendPaidNoCooldownAsync(
                    sender,
                    cost,
                    MinecraftCommandBuilder.Lightning(target.Selector),
                    ct,
                    () => runtime.Commands.ClearTimedCommandCooldown("lightning", reservation)).ConfigureAwait(false))
            {
                return;
            }
            await runtime.SendTellrawAsync(target.Selector, sender + " struck you with lightning!", "yellow", true, ct).ConfigureAwait(false);
            await NotifyOthersAsync(target, "GOT STRUCK BY LIGHTNING!", "yellow", true, ct).ConfigureAwait(false);
            await ConfirmAsync(sender + ", you struck " + TargetName(target) + " with lightning.", ct).ConfigureAwait(false);
        }
        Task LootAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            int times = Random.Shared.Next(3, 5);
            string[] commands = new string[times];
            for (int i = 0; i < times; i++)
            {
                double offsetX = (Random.Shared.NextDouble() * 2.0) - 1.0;
                double offsetZ = (Random.Shared.NextDouble() * 2.0) - 1.0;
                commands[i] = MinecraftCommandBuilder.Loot(target.Selector, runtime.GetRandomLootTable(), offsetX, offsetZ);
            }
            return SendPricedReplyAsync(
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
        Task MobAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string mob = runtime.GetRandomMob();
            string pretty = PrettyName(mob);
            return SendPricedReplyAsync(
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
            if (!await TrySendPricedAsync(sender, runtime.Commands.ScaleCost(15, 1), "time set night", ct).ConfigureAwait(false))
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
            string[] enchantCommands = new string[playerNames.Count];
            (string Player, string Item, string Enchant, int Level, bool HadItem)[] rolls =
                new (string, string, string, int, bool)[playerNames.Count];

            for (int i = 0; i < playerNames.Count; i++)
            {
                string playerName = playerNames[i];
                string? selectedItemData;
                if (selectedItemsByPlayer != null)
                    selectedItemsByPlayer.TryGetValue(playerName, out selectedItemData);
                else
                    selectedItemData = await runtime.QueryItemAsync(playerName, ct).ConfigureAwait(false);
                string singleSelector = MinecraftCommandBuilder.PlayerSelector(playerName);
                Enchantments.Pick(
                    Random.Shared,
                    runtime.SupportsMaceEnchantments,
                    out string enchantID,
                    out string prettyEnchantName,
                    out int level);
                string enchantCommand = string.Empty;
                string prettyItemName = string.Empty;
                bool hadItem = !string.IsNullOrWhiteSpace(selectedItemData) &&
                    ItemComponents.TryBuildEnchantCommand(
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
                    enchantCommand = Enchantments.BuildCommand(singleSelector, enchantID, level);
                }

                enchantCommands[i] = enchantCommand;
                rolls[i] = (playerName, prettyItemName, prettyEnchantName, level, hadItem);
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

            if (rolls.Length == 1)
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
                await ConfirmAsync(sender + ", you rolled random enchantments for " + rolls.Length.ToString(CultureInfo.InvariantCulture) + " players.", ct).ConfigureAwait(false);
            }
            else
            {
                await ConfirmAsync(sender + ", you rolled random enchantments for " + rolls.Length.ToString(CultureInfo.InvariantCulture) + " targets in " + TargetName(target) + ".", ct).ConfigureAwait(false);
            }
        }

        async Task HeartAsync(string[]? args, string sender, bool add, CancellationToken ct)
        {
            if (!int.TryParse(GetArg(args, 0), out int hearts) || hearts is < 1 or > 5 ||
                !await RequireCooldownAsync(sender, ct).ConfigureAwait(false) ||
                !await RequireTokensAsync(sender, hearts * 50, ct).ConfigureAwait(false))
                return;
            ResolvedTarget? target = await ResolveTargetAsync(args, 1, sender, ct).ConfigureAwait(false);
            if (target == null) return;
            List<string> players = await GetPlayersAsync(target, ct).ConfigureAwait(false);
            if (players.Count == 0) return;

            await heartGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (string player in players)
                    await ResetHeartEffectsCoreAsync(player, false, ct).ConfigureAwait(false);
                if (!runtime.Commands.TryUseTimedCommand("heart", out TimeSpan remaining, out long reservation))
                {
                    await SayAsync(sender + ", heart commands are on global cooldown. Try again in " + runtime.FormatCooldown(remaining) + ".", ct).ConfigureAwait(false);
                    return;
                }

                bool sent = false;
                try
                {
                    int delta = hearts * (add ? 2 : -2);
                    foreach (string player in players)
                    {
                        activeHeartEffects.TryGetValue(player, out List<(int Delta, string ID, bool Expired)>? effects);
                        double? health = await runtime.QueryMaxHealthAsync(player, ct).ConfigureAwait(false);
                        if (!health.HasValue) { await SayAsync(sender + ", TwitchCraft could not read " + player + "'s maximum health. You were not charged.", ct).ConfigureAwait(false); return; }
                        double future = health.Value + delta;
                        bool invalid = future is < 10 or > 40;
                        if (!invalid && effects != null) foreach ((int effect, _, _) in effects) if ((future -= effect) is < 10 or > 40) { invalid = true; break; }
                        if (invalid) { await SayAsync(sender + ", that would put " + player + " outside the 5-20 heart limit. You were not charged.", ct).ConfigureAwait(false); return; }
                    }

                    string ID = runtime.UsesNamespacedAttributeModifierIDs ? "twitchcraft:heart_" + Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString();
                    string[] commands = new string[players.Count];
                    for (int i = 0; i < players.Count; i++)
                        commands[i] = MinecraftCommandBuilder.AddMaxHealthModifier(MinecraftCommandBuilder.SinglePlayerSelector(players[i]), ID, delta, runtime.UsesModernAttributeIDs, runtime.UsesNamespacedAttributeModifierIDs);
                    sent = await TrySendPricedAsync(sender, runtime.Commands.ScaleCost(hearts * 50, players.Count), () => commands, ct).ConfigureAwait(false);
                    if (!sent) return;
                    foreach (string player in players) { if (!activeHeartEffects.TryGetValue(player, out List<(int Delta, string ID, bool Expired)>? effects)) activeHeartEffects[player] = effects = []; effects.Add((delta, ID, false)); }
                    runtime.TrackTask(ResetHeartAsync(ID, ct));
                    await ConfirmAsync(sender + ", you " + (add ? "added " : "removed ") + hearts + " max heart" + (hearts == 1 ? "" : "s") + " " + (add ? "to " : "from ") + TargetName(target) + " for 10 minutes.", ct).ConfigureAwait(false);
                }
                finally { if (!sent) runtime.Commands.ClearTimedCommandCooldown("heart", reservation); }
            }
            finally { heartGate.Release(); }
        }

        async Task ResetHeartEffectsAsync(string? player, bool force, CancellationToken ct)
        {
            await heartGate.WaitAsync(ct).ConfigureAwait(false);
            try { await ResetHeartEffectsCoreAsync(player, force, ct).ConfigureAwait(false); }
            finally { heartGate.Release(); }
        }

        async Task ResetHeartEffectsCoreAsync(string? player, bool force, CancellationToken ct)
        {
            if (player != null && await runtime.QueryHeartModifiersAsync(player, ct).ConfigureAwait(false) is { } data)
                SyncHeartEffects(player, MainHandler.ParseHeartModifierIDs(data, runtime.UsesNamespacedAttributeModifierIDs), true);

            List<(string Player, string ID)>? pending = null;
            foreach (var entry in activeHeartEffects)
                for (int i = 0; i < entry.Value.Count; i++)
                {
                    (int delta, string ID, bool expired) = entry.Value[i];
                    if (force && !expired) entry.Value[i] = (delta, ID, expired = true);
                    if (expired && runtime.IsPlayerOnline(entry.Key)) (pending ??= []).Add((entry.Key, ID));
                }
            if (pending == null) return;

            string? selectorPlayer = null, selector = null;
            foreach ((string pendingPlayer, string ID) in pending)
            {
                if (!string.Equals(selectorPlayer, pendingPlayer, StringComparison.OrdinalIgnoreCase))
                {
                    selectorPlayer = pendingPlayer;
                    selector = MinecraftCommandBuilder.SinglePlayerSelector(pendingPlayer);
                }
                _ = await runtime.SendServerCommandAsync(MinecraftCommandBuilder.RemoveMaxHealthModifier(selector!, ID, runtime.UsesModernAttributeIDs), ct).ConfigureAwait(false);
            }

            string? verifiedPlayer = null;
            foreach ((string pendingPlayer, _) in pending)
            {
                if (string.Equals(verifiedPlayer, pendingPlayer, StringComparison.OrdinalIgnoreCase)) continue;
                verifiedPlayer = pendingPlayer;
                if (await runtime.QueryHeartModifiersAsync(pendingPlayer, ct).ConfigureAwait(false) is { } current)
                    SyncHeartEffects(pendingPlayer, MainHandler.ParseHeartModifierIDs(current, runtime.UsesNamespacedAttributeModifierIDs), false);
            }
        }

        void SyncHeartEffects(string player, List<string> current, bool recover)
        {
            if (!activeHeartEffects.TryGetValue(player, out List<(int Delta, string ID, bool Expired)>? effects))
            {
                if (!recover || current.Count == 0) return;
                activeHeartEffects[player] = effects = [];
            }
            for (int i = effects.Count - 1; i >= 0; i--)
                if (!current.Contains(effects[i].ID)) effects.RemoveAt(i);
            if (recover) foreach (string ID in current)
            {
                bool exists = false;
                for (int i = 0; i < effects.Count; i++)
                    if (effects[i].ID == ID) { exists = true; break; }
                if (!exists) effects.Add((0, ID, true));
            }
            if (effects.Count == 0) activeHeartEffects.Remove(player);
        }

        async Task ResetHeartAsync(string ID, CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
                await heartGate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            try
            {
                foreach (List<(int Delta, string ID, bool Expired)> effects in activeHeartEffects.Values)
                    for (int i = 0; i < effects.Count; i++)
                        if (effects[i].ID == ID) effects[i] = (effects[i].Delta, ID, true);
                await ResetHeartEffectsCoreAsync(null, false, CancellationToken.None).ConfigureAwait(false);
            }
            finally { heartGate.Release(); }
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

            if (!runtime.Commands.TryUseTimedCommand(commandName, out TimeSpan remaining, out long reservation))
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
                runtime.Commands.ClearTimedCommandCooldown(commandName, reservation);
                throw;
            }

            if (!sent)
            {
                runtime.Commands.ClearTimedCommandCooldown(commandName, reservation);
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
            List<string> playerNames = await GetPlayersAsync(target, ct).ConfigureAwait(false);
            if (playerNames.Count == 0)
            {
                await SayAsync(sender + ", that player could not be resolved for !rename.", ct).ConfigureAwait(false);
                return;
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
                if (!ItemComponents.TryBuildRenameCommand(singleSelector, selectedItemData, sender, runtime.UsesInlineTextComponentSyntax, out string renameCommand, out string itemName))
                    continue;
                renameCommands.Add(renameCommand);
                renamedPlayers.Add(playerName);
                if (string.IsNullOrWhiteSpace(prettyItemName))
                    prettyItemName = itemName;
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
        Task SwarmAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            var used = new HashSet<string>(5, StringComparer.OrdinalIgnoreCase);
            string[] prettyNames = new string[5];
            string[] swarmCommands = new string[10];
            int count = 0;
            while (count < 5)
            {
                string mob = runtime.GetRandomMob();
                if (!used.Add(mob))
                    continue;
                string pretty = PrettyName(mob);
                prettyNames[count] = pretty;
                swarmCommands[count * 2] = MinecraftCommandBuilder.SummonMob(target.Selector, mob);
                swarmCommands[count * 2 + 1] = MinecraftCommandBuilder.Tellraw(target.Selector, sender + " spawned a " + pretty + " on you.", "yellow", true, runtime.UsesInlineTextComponentSyntax);
                count++;
            }
            return SendPricedReplyAsync(
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
        Task SwitchMilkAsync(ResolvedTarget target, string sender, CancellationToken ct)
        {
            (string itemID, string itemName) = Random.Shared.Next(100) switch
            {
                < 50 => ("minecraft:bucket", "an empty bucket"),
                < 75 => ("minecraft:water_bucket", "a water bucket"),
                _ => ("minecraft:lava_bucket", "a lava bucket")
            };
            string playerName = GetPlayerName(target);
            if (target.PlayerCount == 1 && !IsEveryone(target) && playerName.Length == 0)
                return SayAsync(sender + ", that player could not be resolved for !switchmilk.", ct);
            string switchMilkTag = runtime.Commands.NextSwitchMilkTag();
            string taggedMilkSelector = "@a[tag=" + switchMilkTag + "]";
            List<string> switchMilkCommands = new(7)
            {
                "tag @a remove " + switchMilkTag,
                "execute as " + target.Selector + " if data entity @s Inventory[{id:\"minecraft:milk_bucket\"}] run tag @s add " + switchMilkTag
            };
            if (runtime.MultiTargetingEnabled && target.PlayerCount == 1 && !IsEveryone(target) && runtime.HasOtherPlayer(playerName))
            {
                switchMilkCommands.Add(
                    "execute if entity " + taggedMilkSelector +
                    " run " + MinecraftCommandBuilder.Tellraw(MinecraftCommandBuilder.EveryoneExceptSelector(playerName), ((target.DisplayName ?? playerName).ToUpperInvariant()) + " GOT MILK SWITCHED!", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            }
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run clear @s minecraft:milk_bucket 1");
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run give @s " + itemID + " 1");
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run " + MinecraftCommandBuilder.Tellraw("@s", sender + " transformed one of your milk buckets into " + itemName + "!", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            switchMilkCommands.Add("execute if entity " + taggedMilkSelector + " run tag " + taggedMilkSelector + " remove " + switchMilkTag);
            return SendPricedReplyAsync(
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
            bool thunder = Random.Shared.Next(2) == 0;
            string weatherCommand = thunder ? "weather thunder" : "weather rain";
            if (!await TrySendPricedAsync(sender, runtime.Commands.ScaleCost(10, 1), weatherCommand, ct).ConfigureAwait(false))
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
                _ => GameplayCommands.BuildScared(target.Selector, Random.Shared, runtime.UsesInlineTextComponentSyntax),
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
                _ => GameplayCommands.BuildSlaughter(target.Selector, runtime.MobLootGameRuleName),
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
