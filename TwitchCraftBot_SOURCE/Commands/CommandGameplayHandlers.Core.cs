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
        async Task BanAsync(string[]? args, string sender, CancellationToken ct)
        {
            const string commandName = "ban";
            if (!await RequirePermissionAsync(sender, commandName, ct).ConfigureAwait(false) ||
                !await RequireAdminAsync(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayAsync(sender + ", please provide a valid Minecraft username to ban.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(playerName, runtime.DefaultMinecraftPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SayAsync(sender + ", the streamer account cannot be banned.", ct).ConfigureAwait(false);
                return;
            }
            string reason = args is { Length: > 1 }
                ? string.Join(" ", args, 1, args.Length - 1)
                : string.Empty;
            if (!await runtime.SendServerCommandAsync(MinecraftCommandBuilder.BanPlayer(playerName, reason), ct).ConfigureAwait(false))
            {
                await SayAsync(sender + ", the ban command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }
            await ConfirmAsync(sender + ", banned " + playerName + (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ")."), ct).ConfigureAwait(false);
        }
        async Task CommandStatsAsync(string[]? _, string sender, CancellationToken ct)
        {
            BotStatisticsSnapshot stats = runtime.Statistics.GetSnapshot(ct);
            if (!stats.StatisticsEnabled)
            {
                await SayAsync(sender + ", statistics are disabled in TwitchCraft settings.", ct).ConfigureAwait(false);
                return;
            }

            string mostUsed = stats.SessionMostUsedCommand.Length == 0 ? "none yet" : stats.SessionMostUsedCommand;
            await SuccessAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{sender}, this session: {stats.SessionGameCommandsRun} game commands, {stats.SessionDangerousCommandsRun} dangerous, {stats.SessionNiceCommandsRun} nice, and {stats.SessionTokensSpent} tokens spent. Most used: {mostUsed}."),
                ct).ConfigureAwait(false);
        }
        async Task FollowRewardAsync(string[]? _, string sender, CancellationToken ct)
        {
            if (!runtime.AutomaticFollowRewardsEnabled)
            {
                await SayAsync(sender + ", automatic follow rewards are currently disabled.", ct).ConfigureAwait(false);
                return;
            }

            await SuccessAsync(
                sender + ", following this channel automatically awards " +
                runtime.FollowRewardAmount.ToString(CultureInfo.InvariantCulture) +
                " tokens once per Twitch account. Unfollowing and following again does not award more.",
                ct).ConfigureAwait(false);
        }
        async Task HelpAsync(string[]? _, string sender, CancellationToken ct)
        {
            string details = runtime.MultiTargetingEnabled
                ? "Most commands support targeting: !command player|all|random ... Full list: https://rentry.co/bot-commands"
                : "Use your tokens with these commands: https://rentry.co/bot-commands";
            await SuccessAsync(sender + ". Welcome! Earn tokens by watching the stream. " + details, ct).ConfigureAwait(false);
        }
        async Task PlayerListAsync(string[]? _, string sender, CancellationToken ct)
        {
            List<string> players = await runtime.GetPlayersAsync(ct).ConfigureAwait(false);
            if (players.Count == 0)
            {
                await SuccessAsync(sender + ", there are no players online right now.", ct).ConfigureAwait(false);
                return;
            }
            await SuccessAsync(sender + ", active players (" + players.Count.ToString(CultureInfo.InvariantCulture) + "): " + string.Join(", ", players) + ".", ct).ConfigureAwait(false);
        }
        async Task KickAsync(string[]? args, string sender, CancellationToken ct)
        {
            const string commandName = "kick";
            if (!await RequirePermissionAsync(sender, commandName, ct).ConfigureAwait(false) ||
                !await RequireAdminAsync(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayAsync(sender + ", please provide a valid Minecraft username to kick.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(playerName, runtime.DefaultMinecraftPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SayAsync(sender + ", the streamer account cannot be kicked.", ct).ConfigureAwait(false);
                return;
            }
            string reason = args is { Length: > 1 }
                ? string.Join(" ", args, 1, args.Length - 1)
                : string.Empty;
            if (!await runtime.SendServerCommandAsync(MinecraftCommandBuilder.KickPlayer(playerName, reason), ct).ConfigureAwait(false))
            {
                await SayAsync(sender + ", the kick command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }
            await ConfirmAsync(sender + ", kicked " + playerName + (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ")."), ct).ConfigureAwait(false);
        }
        async Task UnbanAsync(string[]? args, string sender, CancellationToken ct)
        {
            const string commandName = "unban";
            if (!await RequirePermissionAsync(sender, commandName, ct).ConfigureAwait(false) ||
                !await RequireAdminAsync(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayAsync(sender + ", please provide a valid Minecraft username to unban.", ct).ConfigureAwait(false);
                return;
            }
            if (!await runtime.SendServerCommandAsync(MinecraftCommandBuilder.UnbanPlayer(playerName), ct).ConfigureAwait(false))
            {
                await SayAsync(sender + ", the unban command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }
            await ConfirmAsync(sender + ", unbanned " + playerName + ".", ct).ConfigureAwait(false);
        }
        Task WhitelistAddAsync(string[]? args, string sender, CancellationToken ct)
            => WhitelistAsync(args, sender, add: true, ct);

        Task WhitelistRemoveAsync(string[]? args, string sender, CancellationToken ct)
            => WhitelistAsync(args, sender, add: false, ct);

        async Task WhitelistAsync(string[]? args, string sender, bool add, CancellationToken ct)
        {
            string commandName = add ? "whitelistadd" : "whitelistremove";
            string action = add ? "add" : "remove";
            if (!await RequirePermissionAsync(sender, commandName, ct).ConfigureAwait(false) ||
                !await RequireAdminAsync(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (!MinecraftNameHelper.TryNormalizePlayerName(GetArg(args, 0), out string playerName))
            {
                await SayAsync(sender + ", please provide a valid Minecraft username to " + action + " to the whitelist.", ct).ConfigureAwait(false);
                return;
            }
            if (!add && string.Equals(playerName, runtime.DefaultMinecraftPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SayAsync(sender + ", the streamer account cannot be removed from the whitelist.", ct).ConfigureAwait(false);
                return;
            }

            string serverCommand = add
                ? MinecraftCommandBuilder.WhitelistAdd(playerName)
                : MinecraftCommandBuilder.WhitelistRemove(playerName);
            if (!await runtime.SendServerCommandAsync(serverCommand, ct).ConfigureAwait(false))
            {
                await SayAsync(sender + ", the whitelist command could not be sent because the Minecraft server is not ready.", ct).ConfigureAwait(false);
                return;
            }

            string result = add ? "added " + playerName + " to" : "removed " + playerName + " from";
            await ConfirmAsync(sender + ", " + result + " the whitelist.", ct).ConfigureAwait(false);
        }
        async Task EffectAsync(string[]? args, string sender, CancellationToken ct)
        {
            ResolvedTarget? target;
            int count = 1;
            if (IsSingleplayer())
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
                if (!await CheckEffectCountAsync(count, sender, ct).ConfigureAwait(false) ||
                    !await RequireTokensAsync(sender, count, ct).ConfigureAwait(false))
                    return;
                target = await FilterSpectatorsAsync(target, ct).ConfigureAwait(false);
                if (target is null || target.PlayerCount <= 0)
                {
                    await SayAsync(sender + ", no players can be targeted right now.", ct).ConfigureAwait(false);
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
                if (!await CheckEffectCountAsync(count, sender, ct).ConfigureAwait(false) ||
                    !await RequireTokensAsync(sender, count, ct).ConfigureAwait(false))
                    return;
                target = await ResolveTargetAsync(args, argIndex, sender, ct).ConfigureAwait(false);
                if (target == null)
                    return;
            }
            string channelTargetName = TargetName(target);
            int cost = runtime.Commands.ScaleCost(count, target.PlayerCount);
            List<string> effectCommands = new(count);
            List<string> effectNames = new(count);
            for (int i = 0; i < count; i++)
            {
                EffectDefinition effect = runtime.GetRandomEffect();
                int amplifier = BotMainHandler.Randomizer.Next(effect.MinAmplifier, effect.MaxAmplifier + 1);
                int seconds = BotMainHandler.Randomizer.Next(effect.MinSeconds, effect.MaxSeconds + 1);
                string level = EffectLevels[Math.Clamp(amplifier, 0, 4)];
                string effectPretty = PrettyName(effect.ID) + " " + level +
                                      (seconds == 1 ? string.Empty : " for " + seconds.ToString(CultureInfo.InvariantCulture) + " seconds");
                effectNames.Add(effectPretty);
                effectCommands.Add(MinecraftCommandBuilder.ApplyEffect(target.Selector, effect.ID, seconds, amplifier));
            }
            if (!await TrySendPricedAsync(sender, cost, () => effectCommands, ct).ConfigureAwait(false))
                return;
            bool streamerReceivedEffect = await IncludesStreamerAsync(target, ct).ConfigureAwait(false);
            runtime.Statistics.RecordEffects(count, streamerReceivedEffect);
            foreach (string effectPretty in effectNames)
            {
                await runtime.SendTellrawAsync(target.Selector, sender + " gave you " + effectPretty + ".", "yellow", true, ct).ConfigureAwait(false);
                if (count == 1)
                    await ConfirmAsync(sender + ", you gave " + effectPretty + " to " + channelTargetName + ".", ct).ConfigureAwait(false);
            }
            if (count > 1)
                await ConfirmAsync(sender + ", you gave " + count.ToString(CultureInfo.InvariantCulture) + " effects to " + channelTargetName + ".", ct).ConfigureAwait(false);
        }
    }
}
