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
        async Task HandleBan(string[]? args, string sender, CancellationToken ct)
        {
            const string commandName = "ban";
            if (!await RequireAllowed(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!await RequireLocalMultiplayerAdminCommandReady(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayToChannel(sender + ", please provide a valid Minecraft username to ban.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(playerName, runtime.DefaultMinecraftPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SayToChannel(sender + ", the streamer account cannot be banned.", ct).ConfigureAwait(false);
                return;
            }
            string reason = args is { Length: > 1 }
                ? string.Join(" ", args, 1, args.Length - 1)
                : string.Empty;
            if (!await runtime.SendServerCommandAsync(MinecraftCommandBuilder.BanPlayer(playerName, reason), ct).ConfigureAwait(false))
            {
                await SayToChannel(sender + ", the ban command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }
            await SayConfirmationToChannel(sender + ", banned " + playerName + (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ")."), ct).ConfigureAwait(false);
        }
        async Task HandleCommandStats(string[]? _, string sender, CancellationToken ct)
        {
            BotStatisticsSnapshot stats = runtime.GetStatisticsSnapshot(ct);
            if (!stats.StatisticsEnabled)
            {
                await SayToChannel(sender + ", statistics are disabled in TwitchCraft settings.", ct).ConfigureAwait(false);
                return;
            }

            string mostUsed = stats.SessionMostUsedCommand.Length == 0 ? "none yet" : stats.SessionMostUsedCommand;
            await SaySuccessfulToChannel(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{sender}, this session: {stats.SessionGameCommandsRun} game commands, {stats.SessionDangerousCommandsRun} dangerous, {stats.SessionNiceCommandsRun} nice, and {stats.SessionTokensSpent} tokens spent. Most used: {mostUsed}."),
                ct).ConfigureAwait(false);
        }
        async Task HandleFollowReward(string[]? _, string sender, CancellationToken ct)
        {
            if (!runtime.AutomaticFollowRewardsEnabled)
            {
                await SayToChannel(sender + ", automatic follow rewards are currently disabled.", ct).ConfigureAwait(false);
                return;
            }

            await SaySuccessfulToChannel(
                sender + ", following this channel automatically awards " +
                runtime.FollowRewardAmount.ToString(CultureInfo.InvariantCulture) +
                " tokens once per Twitch account. Unfollowing and following again does not award more.",
                ct).ConfigureAwait(false);
        }
        async Task HandleHelp(string[]? _, string sender, CancellationToken ct)
        {
            string details = runtime.MultiTargetingEnabled
                ? "Most commands support targeting: !command player|all|random ... Full list: https://rentry.co/bot-commands"
                : "Use your tokens with these commands: https://rentry.co/bot-commands";
            await SaySuccessfulToChannel(sender + ". Welcome! Earn tokens by watching the stream. " + details, ct).ConfigureAwait(false);
        }
        async Task HandlePlayerList(string[]? _, string sender, CancellationToken ct)
        {
            List<string> players = await runtime.GetOnlinePlayersAsync(ct).ConfigureAwait(false);
            if (players.Count == 0)
            {
                await SaySuccessfulToChannel(sender + ", there are no players online right now.", ct).ConfigureAwait(false);
                return;
            }
            await SaySuccessfulToChannel(sender + ", active players (" + players.Count.ToString(CultureInfo.InvariantCulture) + "): " + string.Join(", ", players) + ".", ct).ConfigureAwait(false);
        }
        async Task HandleKick(string[]? args, string sender, CancellationToken ct)
        {
            const string commandName = "kick";
            if (!await RequireAllowed(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!await RequireLocalMultiplayerAdminCommandReady(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayToChannel(sender + ", please provide a valid Minecraft username to kick.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(playerName, runtime.DefaultMinecraftPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SayToChannel(sender + ", the streamer account cannot be kicked.", ct).ConfigureAwait(false);
                return;
            }
            string reason = args is { Length: > 1 }
                ? string.Join(" ", args, 1, args.Length - 1)
                : string.Empty;
            if (!await runtime.SendServerCommandAsync(MinecraftCommandBuilder.KickPlayer(playerName, reason), ct).ConfigureAwait(false))
            {
                await SayToChannel(sender + ", the kick command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }
            await SayConfirmationToChannel(sender + ", kicked " + playerName + (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ")."), ct).ConfigureAwait(false);
        }
        async Task HandleUnban(string[]? args, string sender, CancellationToken ct)
        {
            const string commandName = "unban";
            if (!await RequireAllowed(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!await RequireLocalMultiplayerAdminCommandReady(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayToChannel(sender + ", please provide a valid Minecraft username to unban.", ct).ConfigureAwait(false);
                return;
            }
            if (!await runtime.SendServerCommandAsync(MinecraftCommandBuilder.UnbanPlayer(playerName), ct).ConfigureAwait(false))
            {
                await SayToChannel(sender + ", the unban command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }
            await SayConfirmationToChannel(sender + ", unbanned " + playerName + ".", ct).ConfigureAwait(false);
        }
        Task HandleWhitelistAdd(string[]? args, string sender, CancellationToken ct)
            => HandleWhitelistChange(args, sender, add: true, ct);

        Task HandleWhitelistRemove(string[]? args, string sender, CancellationToken ct)
            => HandleWhitelistChange(args, sender, add: false, ct);

        async Task HandleWhitelistChange(string[]? args, string sender, bool add, CancellationToken ct)
        {
            string commandName = add ? "whitelistadd" : "whitelistremove";
            string action = add ? "add" : "remove";
            if (!await RequireAllowed(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!await RequireLocalMultiplayerAdminCommandReady(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayToChannel(sender + ", please provide a valid Minecraft username to " + action + " to the whitelist.", ct).ConfigureAwait(false);
                return;
            }
            if (!add && string.Equals(playerName, runtime.DefaultMinecraftPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SayToChannel(sender + ", the streamer account cannot be removed from the whitelist.", ct).ConfigureAwait(false);
                return;
            }

            string serverCommand = add
                ? MinecraftCommandBuilder.WhitelistAdd(playerName)
                : MinecraftCommandBuilder.WhitelistRemove(playerName);
            if (!await runtime.SendServerCommandAsync(serverCommand, ct).ConfigureAwait(false))
            {
                await SayToChannel(sender + ", the whitelist command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }

            string result = add ? "added " + playerName + " to" : "removed " + playerName + " from";
            await SayConfirmationToChannel(sender + ", " + result + " the whitelist.", ct).ConfigureAwait(false);
        }
        async Task HandleEffect(string[]? args, string sender, CancellationToken ct)
        {
            ResolvedTarget? target;
            int count = 1;
            if (SingleplayerTargetingMode())
            {
                if (args is { Length: >= 1 } &&
                    int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
                {
                    count = parsedCount;
                }
                target = new ResolvedTarget
                {
                    Selector = "@a[gamemode=!spectator]",
                    DisplayName = string.IsNullOrEmpty(runtime.DefaultMinecraftPlayer) ? "everyone" : runtime.DefaultMinecraftPlayer,
                    PlayerCount = 1
                };
                if (!await ValidateEffectCount(count, sender, ct).ConfigureAwait(false))
                    return;
                if (!await RequireTokenBalance(sender, count, ct).ConfigureAwait(false))
                    return;
                target = await ApplySpectatorFilter(target, ct).ConfigureAwait(false);
                if (target is null || target.PlayerCount <= 0)
                {
                    await SayToChannel(sender + ", no players can be targeted right now.", ct).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                int argIndex = 0;
                if (args is { Length: > 0 } && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
                {
                    count = parsedCount;
                    argIndex = 1;
                }
                if (!await ValidateEffectCount(count, sender, ct).ConfigureAwait(false))
                    return;
                if (!await RequireTokenBalance(sender, count, ct).ConfigureAwait(false))
                    return;
                target = await ResolveTargetAt(args, argIndex, sender, ct).ConfigureAwait(false);
                if (target == null)
                    return;
            }
            string channelTargetName = TargetName(target);
            int cost = runtime.ScaleCost(count, target.PlayerCount);
            List<string> effectCommands = new(count);
            List<string> effectNames = new(count);
            for (int i = 0; i < count; i++)
            {
                EffectDefinition effect = runtime.GetRandomEffect();
                int amplifier = BotMainHandler.Randomizer.Next(effect.MinAmplifier, effect.MaxAmplifier + 1);
                int seconds = BotMainHandler.Randomizer.Next(effect.MinSeconds, effect.MaxSeconds + 1);
                string level = EffectLevels[Math.Clamp(amplifier, 0, 4)];
                string effectPretty = PrettyMinecraftName(effect.ID) + " " + level +
                                      (seconds == 1 ? string.Empty : " for " + seconds.ToString(CultureInfo.InvariantCulture) + " seconds");
                effectNames.Add(effectPretty);
                effectCommands.Add(MinecraftCommandBuilder.ApplyEffect(target.Selector, effect.ID, seconds, amplifier));
            }
            if (!await TrySendPricedCommands(sender, cost, () => effectCommands, ct).ConfigureAwait(false))
                return;
            bool streamerReceivedEffect = await TargetIncludesStreamerAsync(target, ct).ConfigureAwait(false);
            runtime.RecordEffectsGivenForStatistics(count, streamerReceivedEffect);
            foreach (string effectPretty in effectNames)
            {
                await runtime.SendTellrawAsync(target.Selector, sender + " gave you " + effectPretty + ".", "yellow", true, ct).ConfigureAwait(false);
                if (count == 1)
                    await SayConfirmationToChannel(sender + ", you gave " + effectPretty + " to " + channelTargetName + ".", ct).ConfigureAwait(false);
            }
            if (count > 1)
                await SayConfirmationToChannel(sender + ", you gave " + count.ToString(CultureInfo.InvariantCulture) + " effects to " + channelTargetName + ".", ct).ConfigureAwait(false);
        }
    }
}
