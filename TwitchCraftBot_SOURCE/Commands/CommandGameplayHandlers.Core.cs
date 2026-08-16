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
            await SayToChannel(sender + ", banned " + playerName + (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ")."), ct).ConfigureAwait(false);
        }
        async Task HandleHelp(string[]? _, string sender, CancellationToken ct)
        {
            string details = runtime.MultiTargetingEnabled
                ? "Most commands support targeting: !command player|all|random ... Full list: https://rentry.co/bot-commands"
                : "Use your tokens with these commands: https://rentry.co/bot-commands";
            await SayToChannel(sender + ". Welcome! Earn tokens by watching the stream. " + details, ct).ConfigureAwait(false);
        }
        async Task HandlePlayerList(string[]? _, string sender, CancellationToken ct)
        {
            List<string> players = await runtime.GetOnlinePlayersAsync(ct).ConfigureAwait(false);
            if (players.Count == 0)
            {
                await SayToChannel(sender + ", there are no players online right now.", ct).ConfigureAwait(false);
                return;
            }
            await SayToChannel(sender + ", active players (" + players.Count.ToString(CultureInfo.InvariantCulture) + "): " + string.Join(", ", players) + ".", ct).ConfigureAwait(false);
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
            await SayToChannel(sender + ", unbanned " + playerName + ".", ct).ConfigureAwait(false);
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
                int amplifier = BotMainHandler.SecureRandomInt(effect.MinAmplifier, effect.MaxAmplifier + 1);
                int seconds = BotMainHandler.SecureRandomInt(effect.MinSeconds, effect.MaxSeconds + 1);
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
                    await SayToChannel(sender + ", you gave " + effectPretty + " to " + channelTargetName + ".", ct).ConfigureAwait(false);
            }
            if (count > 1)
                await SayToChannel(sender + ", you gave " + count.ToString(CultureInfo.InvariantCulture) + " effects to " + channelTargetName + ".", ct).ConfigureAwait(false);
        }
    }
}
