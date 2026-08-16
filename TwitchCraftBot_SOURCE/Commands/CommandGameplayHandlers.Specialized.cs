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
        async Task HandleFireworks(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string fireworkCommand = "execute at " + target.Selector + " run summon firework_rocket ~ ~1 ~ {LifeTime:20}";
            if (!await SendSingleTargetedPricedCommand(target, sender, 10, fireworkCommand, ct, sender + " sent you some fireworks.", "GOT FIREWORKS!").ConfigureAwait(false))
                return;
            await SayToChannel(sender + ", you sent " + TargetName(target) + " some fireworks.", ct).ConfigureAwait(false);
            if (!runtime.TryBeginFireworksRepeat())
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
                    runtime.AddServerLogLine(ErrorHandling.FormatLogMessage("Fireworks repeat failed", ex));
                }
                finally
                {
                    runtime.EndFireworksRepeat();
                }
            }, CancellationToken.None);
            runtime.TrackSessionBackgroundTask(fireworksRepeatTask);
        }
        async Task HandleInsult(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string color = InsultTitleColors[BotMainHandler.SecureRandomInt(InsultTitleColors.Length)];
            await SendTargetedPricedCommandAndSay(
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
        async Task HandleJohnny(ResolvedTarget target, string sender, CancellationToken ct)
        {
            List<string> commands = MinecraftCommandFeatureBuilder.BuildJohnnyCommands(target.Selector, runtime.UsesInlineTextComponentSyntax, runtime.UsesModernEntityAttributeNbt);
            commands.Add(MinecraftCommandBuilder.TitleTimes(target.Selector, 0, 100, 10));
            commands.Add(MinecraftCommandBuilder.Title(target.Selector, " ", "white", runtime.UsesInlineTextComponentSyntax));
            commands.Add(MinecraftCommandBuilder.Subtitle(target.Selector, "Johnny is coming!", "red", runtime.UsesInlineTextComponentSyntax));
            await SendTargetedPricedCommandAndSay(
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
        async Task HandleLightning(string[]? args, string sender, CancellationToken ct)
        {
            if (!await RequireMinecraftReady(sender, ct).ConfigureAwait(false))
                return;
            if (!runtime.TryUseLightning(out TimeSpan remaining, out DateTime lightningReservationUtc))
            {
                await SayToChannel(sender + ", command is on global cooldown. Try again in " + FormatMinutesSeconds(remaining) + ".", ct).ConfigureAwait(false);
                return;
            }
            ResolvedTarget? target;
            try
            {
                target = await PrepareTargetedCommand(args, sender, ct, checkGameCooldown: false, minimumTokenCost: 50).ConfigureAwait(false);
            }
            catch
            {
                runtime.ClearLightningCooldown(lightningReservationUtc);
                throw;
            }
            if (target == null)
            {
                runtime.ClearLightningCooldown(lightningReservationUtc);
                return;
            }
            int cost = runtime.ScaleCost(50, target.PlayerCount);
            if (!await TrySendPaidCommandWithoutGameCooldown(
                    sender,
                    cost,
                    MinecraftCommandBuilder.Lightning(target.Selector),
                    ct,
                    () => runtime.ClearLightningCooldown(lightningReservationUtc)).ConfigureAwait(false))
            {
                return;
            }
            await runtime.SendTellrawAsync(target.Selector, sender + " struck you with lightning!", "yellow", true, ct).ConfigureAwait(false);
            await TellOthers(target, "GOT STRUCK BY LIGHTNING!", "yellow", true, ct).ConfigureAwait(false);
            await SayToChannel(sender + ", you struck " + TargetName(target) + " with lightning.", ct).ConfigureAwait(false);
        }
        async Task HandleLoot(ResolvedTarget target, string sender, CancellationToken ct)
        {
            int times = BotMainHandler.SecureRandomInt(3, 5);
            var commands = new List<string>(times);
            for (int i = 0; i < times; i++)
            {
                double offsetX = (BotMainHandler.SecureRandomDouble() * 2.0) - 1.0;
                double offsetZ = (BotMainHandler.SecureRandomDouble() * 2.0) - 1.0;
                commands.Add(MinecraftCommandBuilder.Loot(target.Selector, runtime.GetRandomLootTable(), offsetX, offsetZ));
            }
            await SendTargetedPricedCommandAndSay(
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
        async Task HandleMob(ResolvedTarget target, string sender, CancellationToken ct)
        {
            string mob = runtime.GetRandomMob();
            string pretty = PrettyMinecraftName(mob);
            await SendSingleTargetedPricedCommandAndSay(
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
        async Task HandleNight(string[]? _, string sender, CancellationToken ct)
        {
            if (!await TrySendPricedCommand(sender, 15, "time set night", ct).ConfigureAwait(false))
                return;
            await runtime.SendTellrawAsync("@a", sender + " made it night.", "yellow", true, ct).ConfigureAwait(false);
            await SayToChannel(sender + ", you changed the time to night.", ct).ConfigureAwait(false);
        }
        async Task HandleRename(string[]? args, string sender, CancellationToken ct)
        {
            ResolvedTarget? target = await PrepareTargetedCommand(args, sender, ct, minimumTokenCost: 10).ConfigureAwait(false);
            if (target == null)
                return;
            bool targetsEveryone = TargetsEveryone(target);
            List<string> playerNames;
            if (targetsEveryone || target.PlayerCount > 1)
            {
                playerNames = NormalizePlayerTargets(target.TargetablePlayers ?? await runtime.GetOnlinePlayersAsync(ct).ConfigureAwait(false));
            }
            else
            {
                string playerName = GetSingleTargetMinecraftName(target).Trim();
                if (!MinecraftNameHelper.IsValidPlayerName(playerName))
                {
                    await SayToChannel(sender + ", that player could not be resolved for !rename.", ct).ConfigureAwait(false);
                    return;
                }
                playerNames = [playerName];
            }
            List<string> renameCommands = new(playerNames.Count);
            List<string> renamedPlayers = new(playerNames.Count);
            string prettyItemName = string.Empty;
            Dictionary<string, string?>? selectedItemsByPlayer = playerNames.Count > 1
                ? await runtime.QuerySelectedItemDataBatchAsync(playerNames, ct).ConfigureAwait(false)
                : null;
            foreach (string playerName in playerNames)
            {
                string? selectedItemData;
                if (selectedItemsByPlayer != null)
                    selectedItemsByPlayer.TryGetValue(playerName, out selectedItemData);
                else
                    selectedItemData = await runtime.QuerySelectedItemDataAsync(playerName, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(selectedItemData))
                    continue;
                string singleSelector = MinecraftCommandBuilder.PlayerSelector(playerName);
                if (!MinecraftItemRenameHelper.TryBuildRenameCommand(singleSelector, selectedItemData, sender, runtime.UsesItemComponentsSyntax, runtime.UsesInlineTextComponentSyntax, out string renameCommand, out string currentPrettyItemName))
                    continue;
                renameCommands.Add(renameCommand);
                renamedPlayers.Add(playerName);
                if (string.IsNullOrWhiteSpace(prettyItemName))
                    prettyItemName = currentPrettyItemName;
            }
            if (renameCommands.Count == 0)
            {
                await SayToChannel(sender + ", " + TargetName(target) + " is not holding a renameable item right now.", ct).ConfigureAwait(false);
                return;
            }
            int cost = runtime.ScaleCost(10, renameCommands.Count);
            if (!await TrySendPricedCommands(sender, cost, () => renameCommands, ct).ConfigureAwait(false))
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
                await SayToChannel(sender + ", you renamed " + renamedPlayers[0] + "'s held " + prettyItemName + ".", ct).ConfigureAwait(false);
            else if (targetsEveryone)
                await SayToChannel(sender + ", you renamed " + renamedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " players' held items.", ct).ConfigureAwait(false);
            else
                await SayToChannel(sender + ", you renamed " + renamedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " held items for " + TargetName(target) + ".", ct).ConfigureAwait(false);
        }
        async Task HandleSwarm(ResolvedTarget target, string sender, CancellationToken ct)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prettyNames = new List<string>(5);
            var swarmCommands = new List<string>(10);
            while (prettyNames.Count < 5)
            {
                string mob = runtime.GetRandomMob();
                if (!used.Add(mob))
                    continue;
                string pretty = PrettyMinecraftName(mob);
                prettyNames.Add(pretty);
                swarmCommands.Add(MinecraftCommandBuilder.SummonMob(target.Selector, mob));
                swarmCommands.Add(MinecraftCommandBuilder.Tellraw(target.Selector, sender + " spawned a " + pretty + " on you.", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            }
            await SendTargetedPricedCommandAndSay(
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
        async Task HandleSwitchMilk(ResolvedTarget target, string sender, CancellationToken ct)
        {
            (string itemID, string itemName) = BotMainHandler.SecureRandomInt(100) switch
            {
                < 50 => ("minecraft:bucket", "an empty bucket"),
                < 75 => ("minecraft:water_bucket", "a water bucket"),
                _ => ("minecraft:lava_bucket", "a lava bucket")
            };
            string singleMilkTargetName = GetSingleTargetMinecraftName(target);
            if (target.PlayerCount == 1 && !TargetsEveryone(target) && !MinecraftNameHelper.IsValidPlayerName(singleMilkTargetName))
            {
                await SayToChannel(sender + ", that player could not be resolved for !switchmilk.", ct).ConfigureAwait(false);
                return;
            }
            string switchMilkTag = runtime.CreateSwitchMilkTag();
            string taggedMilkSelector = "@a[tag=" + switchMilkTag + "]";
            List<string> switchMilkCommands = ["tag @a remove " + switchMilkTag];
            switchMilkCommands.Add("execute as " + target.Selector + " if data entity @s Inventory[{id:\"minecraft:milk_bucket\"}] run tag @s add " + switchMilkTag);
            if (runtime.MultiTargetingEnabled && target.PlayerCount == 1 && !TargetsEveryone(target) && runtime.HasOtherKnownPlayer(singleMilkTargetName))
            {
                switchMilkCommands.Add(
                    "execute if entity " + taggedMilkSelector +
                    " run " + MinecraftCommandBuilder.Tellraw(MinecraftCommandBuilder.AllExceptPlayerSelector(singleMilkTargetName), ((target.DisplayName ?? singleMilkTargetName).ToUpperInvariant()) + " GOT MILK SWITCHED!", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            }
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run clear @s minecraft:milk_bucket 1");
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run give @s " + itemID + " 1");
            switchMilkCommands.Add("execute as " + taggedMilkSelector + " run " + MinecraftCommandBuilder.Tellraw("@s", sender + " transformed one of your milk buckets into " + itemName + "!", "yellow", true, runtime.UsesInlineTextComponentSyntax));
            switchMilkCommands.Add("execute if entity " + taggedMilkSelector + " run tag " + taggedMilkSelector + " remove " + switchMilkTag);
            await SendTargetedPricedCommandAndSay(
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
        async Task HandleWeather(string[]? _, string sender, CancellationToken ct)
        {
            bool thunder = BotMainHandler.SecureRandomInt(2) == 0;
            string weatherCommand = thunder ? "weather thunder" : "weather rain";
            if (!await TrySendPricedCommand(sender, 10, weatherCommand, ct).ConfigureAwait(false))
                return;
            string weatherAction = thunder ? "started a thunderstorm" : "made it rain";
            await runtime.SendTellrawAsync("@a", sender + " " + weatherAction + ".", "yellow", true, ct).ConfigureAwait(false);
            await SayToChannel(sender + ", you " + weatherAction + ".", ct).ConfigureAwait(false);
        }
        Task HandleMlg(ResolvedTarget target, string sender, CancellationToken ct)
            => SendTargetedPricedCommandAndSay(
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
        Task HandleScared(ResolvedTarget target, string sender, CancellationToken ct)
            => SendTargetedPricedCommandAndSay(
                target,
                sender,
                15,
                _ => MinecraftCommandFeatureBuilder.BuildScaredCommands(target.Selector, runtime.UsesInlineTextComponentSyntax),
                sender + " thinks you're a scaredy cat and spawned cats above you.",
                "GOT BURIED IN CATS!",
                sender + ", you spawned 20 cats on " + TargetName(target) + ".",
                "yellow",
                true,
                null,
                ct);
        Task HandleSlaughter(ResolvedTarget target, string sender, CancellationToken ct)
            => SendTargetedPricedCommandAndSay(
                target,
                sender,
                30,
                _ => MinecraftCommandFeatureBuilder.BuildSlaughterCommands(target.Selector, runtime.MobLootGameRuleName),
                sender + " slaughtered any nearby mobs.",
                "GOT THEIR AREA SLAUGHTERED!",
                sender + ", you slaughtered any nearby mobs around " + TargetName(target) + ".",
                "yellow",
                true,
                null,
                ct);
    }
}
