using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
namespace TwitchCraftBot_V1;

public static class CommandList
{
    private static readonly string[] InsultTitleColors = ["red", "gold", "yellow", "green", "aqua", "blue", "light_purple", "white"];
    private static readonly string[] EffectLevels = ["I", "II", "III", "IV", "V"];
    private static readonly TimeSpan GambleTokenCooldown = TimeSpan.FromMinutes(5);
    public static Dictionary<string, ChatCommandHandler> BuildCommandHandlers(BotMainHandler runtime, Dictionary<string, ChatCommandStatisticFlags>? statisticFlags = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var handlers = new Dictionary<string, ChatCommandHandler>(64, StringComparer.OrdinalIgnoreCase);
        const int MaxEffectCount = 25;
        const ChatCommandStatisticFlags GameCommand = ChatCommandStatisticFlags.GameAffecting;
        const ChatCommandStatisticFlags DangerousCommand = ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Dangerous;
        const ChatCommandStatisticFlags NiceCommand = ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Nice;
        void AddCommand(string commandName, ChatCommandHandler handler, ChatCommandStatisticFlags commandStatisticFlags = ChatCommandStatisticFlags.None)
        {
            handlers[commandName] = handler;
            if (commandStatisticFlags != ChatCommandStatisticFlags.None)
                statisticFlags?[commandName] = commandStatisticFlags;
        }
        static List<string> NormalizePlayerTargets(List<string>? players)
            => SortedListHelper.NormalizeMinecraftPlayerNames(players, StringComparer.OrdinalIgnoreCase);
        Task SayToChannel(string? msg, CancellationToken ct)
            => string.IsNullOrWhiteSpace(msg) ? Task.CompletedTask : runtime.SendToChannelAsync(msg, ct);
        Task SayInsufficientTokens(string sender, int cost, CancellationToken ct)
            => SayToChannel(sender + ", you need at least " + cost.ToString(CultureInfo.InvariantCulture) + " tokens for this command.", ct);
        async Task<bool> RequireLocalMultiplayerAdminCommandReady(string sender, string commandName, CancellationToken ct)
        {
            if (runtime.RemoteControlEnabled || !runtime.MultiplayerEnabled)
            {
                string mode = runtime.RemoteControlEnabled ? "Remote Control Mode" : "singleplayer mode";
                await SayToChannel(sender + ", !" + commandName + " is not available in " + mode + ".", ct).ConfigureAwait(false);
                return false;
            }
            if (runtime.MinecraftServerReady)
                return true;
            await SayToChannel(sender + ", the Minecraft server is still starting. Try again in a moment.", ct).ConfigureAwait(false);
            return false;
        }
        static string GetArg(IReadOnlyList<string>? args, int index)
        {
            if (args is null || index < 0 || index >= args.Count)
                return string.Empty;
            return args[index] ?? string.Empty;
        }
        async Task<bool> RequireTokens(string sender, int cost, CancellationToken ct)
        {
            if (cost <= 0)
                return true;
            if (runtime.TrySpendTokens(sender, cost))
                return true;
            await SayInsufficientTokens(sender, cost, ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> RequireTokenBalance(string sender, int cost, CancellationToken ct)
        {
            if (cost <= 0 || runtime.GetTokens(sender) >= cost) return true;
            await SayInsufficientTokens(sender, cost, ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> TrySendPaidCommandsWithoutGameCooldown(string sender, int cost, Func<IEnumerable<string>> buildCommands, CancellationToken ct, Action? onSendFailure = null)
        {
            if (!await RequireTokens(sender, cost, ct).ConfigureAwait(false))
            {
                onSendFailure?.Invoke();
                return false;
            }
            IEnumerable<string> commands;
            try
            {
                commands = buildCommands();
            }
            catch
            {
                if (cost > 0)
                    runtime.AdjustTokens(sender, cost);
                onSendFailure?.Invoke();
                throw;
            }
            if (await runtime.SendServerCommandsAsync(commands, ct).ConfigureAwait(false))
            {
                runtime.RecordCurrentGameAffectingCommandForStatistics(sender, cost);
                return true;
            }
            runtime.AdjustTokens(sender, cost);
            onSendFailure?.Invoke();
            await SayToChannel(sender + ", the Minecraft command could not be sent, so your tokens were refunded.", ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> TrySendPaidCommandWithoutGameCooldown(string sender, int cost, string command, CancellationToken ct, Action? onSendFailure = null)
        {
            if (!await RequireTokens(sender, cost, ct).ConfigureAwait(false))
            {
                onSendFailure?.Invoke();
                return false;
            }
            if (await runtime.SendServerCommandAsync(command, ct).ConfigureAwait(false))
            {
                runtime.RecordCurrentGameAffectingCommandForStatistics(sender, cost);
                return true;
            }
            runtime.AdjustTokens(sender, cost);
            onSendFailure?.Invoke();
            await SayToChannel(sender + ", the Minecraft command could not be sent, so your tokens were refunded.", ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> TrySendPricedCommands(string sender, int cost, Func<IEnumerable<string>> buildCommands, CancellationToken ct)
        {
            long? cooldownReservation = await TryReserveGameCommandCooldown(sender, ct).ConfigureAwait(false);
            if (!cooldownReservation.HasValue)
                return false;
            try
            {
                if (await TrySendPaidCommandsWithoutGameCooldown(sender, cost, buildCommands, ct).ConfigureAwait(false))
                    return true;
                runtime.ClearGlobalGameCommandCooldown(cooldownReservation.Value);
                return false;
            }
            catch
            {
                runtime.ClearGlobalGameCommandCooldown(cooldownReservation.Value);
                throw;
            }
        }
        async Task<bool> TrySendPricedCommand(string sender, int cost, string command, CancellationToken ct)
        {
            long? cooldownReservation = await TryReserveGameCommandCooldown(sender, ct).ConfigureAwait(false);
            if (!cooldownReservation.HasValue)
                return false;
            try
            {
                if (await TrySendPaidCommandWithoutGameCooldown(sender, cost, command, ct).ConfigureAwait(false))
                    return true;
                runtime.ClearGlobalGameCommandCooldown(cooldownReservation.Value);
                return false;
            }
            catch
            {
                runtime.ClearGlobalGameCommandCooldown(cooldownReservation.Value);
                throw;
            }
        }
        static bool TargetsEveryone(ResolvedTarget? target)
        {
            if (target == null || string.IsNullOrEmpty(target.Selector))
                return false;
            string selector = target.Selector.Trim();
            return string.Equals(selector, "@a", StringComparison.OrdinalIgnoreCase) ||
                selector.StartsWith("@a[", StringComparison.OrdinalIgnoreCase) &&
                !selector.Contains("name=", StringComparison.OrdinalIgnoreCase) &&
                !selector.Contains("name!=", StringComparison.OrdinalIgnoreCase) &&
                !selector.Contains("limit=1", StringComparison.OrdinalIgnoreCase);
        }
        static bool ContainsPlayer(IReadOnlyList<string> players, string playerName)
            => SortedListHelper.Contains(players, playerName, StringComparer.OrdinalIgnoreCase);
        bool SingleplayerTargetingMode() => !runtime.MultiTargetingEnabled;
        async Task<ResolvedTarget?> ApplySpectatorFilter(ResolvedTarget? target, CancellationToken ct, List<string>? cachedTargetablePlayers = null)
        {
            if (target == null)
                return null;
            List<string> activePlayers = cachedTargetablePlayers ??
                NormalizePlayerTargets(await runtime.GetOnlinePlayersAsync(ct).ConfigureAwait(false));
            string defaultMinecraftPlayer = runtime.DefaultMinecraftPlayerName;
            bool defaultPlayerIsValid = defaultMinecraftPlayer.Length > 0;
            if (SingleplayerTargetingMode() && activePlayers.Count == 0 && defaultPlayerIsValid)
                activePlayers = [defaultMinecraftPlayer];
            int activeCount = activePlayers.Count;
            bool activePlayersIncludeDefault = defaultPlayerIsValid
                && ContainsPlayer(activePlayers, defaultMinecraftPlayer);
            bool everyone = TargetsEveryone(target);
            target.TargetablePlayers = activePlayers;
            if (everyone)
            {
                target.Selector = "@a[gamemode=!spectator]";
                target.PlayerCount = activeCount;
                target.DefaultPlayerInclusionKnown = true;
                target.IncludesDefaultMinecraftPlayer = activePlayersIncludeDefault || (SingleplayerTargetingMode() && defaultPlayerIsValid);
                if (SingleplayerTargetingMode() && !string.IsNullOrWhiteSpace(runtime.StreamerName))
                    target.DisplayName = runtime.StreamerName;
                else if (activeCount == 1)
                    target.DisplayName = activePlayers[0];
                else if (string.IsNullOrWhiteSpace(target.DisplayName))
                    target.DisplayName = "everyone";
                return target;
            }
            string targetName = string.IsNullOrWhiteSpace(target.MinecraftName)
                ? (target.DisplayName ?? string.Empty).Trim()
                : target.MinecraftName.Trim();
            if (targetName.Length > 0)
            {
                bool targetIsActive = activeCount > 0 && ContainsPlayer(activePlayers, targetName);
                target.MinecraftName = targetName;
                target.Selector = MinecraftCommandBuilder.PlayerSelector(targetName);
                target.PlayerCount = targetIsActive ? 1 : 0;
                target.DefaultPlayerInclusionKnown = true;
                target.IncludesDefaultMinecraftPlayer = targetIsActive
                    && defaultPlayerIsValid
                    && string.Equals(targetName, defaultMinecraftPlayer, StringComparison.OrdinalIgnoreCase);
                target.TargetablePlayers = targetIsActive ? [targetName] : [];
                return target;
            }
            string selector = (target.Selector ?? string.Empty).Trim();
            if (selector.Length == 0)
            {
                target.Selector = "@a[gamemode=!spectator]";
                target.PlayerCount = activeCount;
                target.DefaultPlayerInclusionKnown = true;
                target.IncludesDefaultMinecraftPlayer = activePlayersIncludeDefault || (SingleplayerTargetingMode() && defaultPlayerIsValid);
                if (string.IsNullOrWhiteSpace(target.DisplayName))
                    target.DisplayName = activeCount == 1 ? activePlayers[0] : "everyone";
                return target;
            }
            if (selector.Contains("gamemode=!spectator", StringComparison.OrdinalIgnoreCase))
            {
                target.PlayerCount = Math.Min(target.PlayerCount, activeCount);
                return target;
            }
            if (selector.EndsWith(']'))
                target.Selector = selector[..^1] + ",gamemode=!spectator]";
            else if (selector.StartsWith('@'))
                target.Selector = selector + "[gamemode=!spectator]";
            target.PlayerCount = Math.Min(target.PlayerCount, activeCount);
            return target;
        }
        async Task<bool> TargetIncludesStreamerAsync(ResolvedTarget target, CancellationToken ct)
        {
            string streamerMinecraftName = runtime.DefaultMinecraftPlayerName;
            if (streamerMinecraftName.Length == 0)
                return false;
            if (!string.IsNullOrWhiteSpace(target.MinecraftName) &&
                string.Equals(target.MinecraftName.Trim(), streamerMinecraftName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (target.DefaultPlayerInclusionKnown)
            {
                return target.IncludesDefaultMinecraftPlayer;
            }
            string selector = (target.Selector ?? string.Empty).Trim();
            if (!selector.StartsWith("@a", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            List<string> targetablePlayers = NormalizePlayerTargets(target.TargetablePlayers ?? await runtime.GetOnlinePlayersAsync(ct).ConfigureAwait(false));
            return ContainsPlayer(targetablePlayers, streamerMinecraftName);
        }
        async Task<ResolvedTarget?> ResolveTargetAt(IReadOnlyList<string>? args, int startIndex, string sender, CancellationToken ct)
        {
            if (args is not null && startIndex >= 0 && startIndex < args.Count)
            {
                string first = (args[startIndex] ?? string.Empty).Trim();
                if (first.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> players = NormalizePlayerTargets(await runtime.GetOnlinePlayersAsync(ct).ConfigureAwait(false));
                    string defaultPlayer = runtime.DefaultMinecraftPlayerName;
                    if (players.Count == 0 &&
                        SingleplayerTargetingMode() &&
                        defaultPlayer.Length > 0)
                    {
                        players = [defaultPlayer];
                    }
                    if (players.Count == 0)
                    {
                        await SayToChannel(sender + ", no players are online to target right now.", ct).ConfigureAwait(false);
                        return null;
                    }
                    string chosen = players[BotMainHandler.Randomizer.Next(players.Count)];
                    ResolvedTarget? randomTarget = new()
                    {
                        Selector = MinecraftCommandBuilder.PlayerSelector(chosen),
                        DisplayName = chosen,
                        MinecraftName = chosen,
                        PlayerCount = 1
                    };
                    randomTarget = await ApplySpectatorFilter(randomTarget, ct, players).ConfigureAwait(false);
                    if (randomTarget is null || randomTarget.PlayerCount <= 0)
                    {
                        await SayToChannel(sender + ", no players can be targeted right now.", ct).ConfigureAwait(false);
                        return null;
                    }
                    if (SingleplayerTargetingMode() && !string.IsNullOrEmpty(runtime.StreamerName))
                        randomTarget.DisplayName = runtime.StreamerName;
                    return randomTarget;
                }
            }
            ResolvedTarget? resolved = await runtime.ResolveTargetAsync(
                args,
                startIndex,
                sender,
                SayToChannel,
                ct).ConfigureAwait(false);
            ResolvedTarget? target = await ApplySpectatorFilter(resolved, ct).ConfigureAwait(false);
            if (target == null)
                return null;
            if (target.PlayerCount <= 0)
            {
                string failure = TargetsEveryone(target)
                    ? sender + ", no players can be targeted right now."
                    : sender + ", that player is spectating or unavailable and cannot be targeted.";
                await SayToChannel(failure, ct).ConfigureAwait(false);
                return null;
            }
            if (SingleplayerTargetingMode() &&
                !string.IsNullOrEmpty(runtime.StreamerName) &&
                target.PlayerCount == 1 &&
                !TargetsEveryone(target) &&
                !string.Equals(target.DisplayName, "everyone", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(target.MinecraftName))
                    target.MinecraftName = (target.DisplayName ?? string.Empty).Trim();
                target.DisplayName = runtime.StreamerName;
            }
            return target;
        }
        async Task<ResolvedTarget?> PrepareTargetedCommand(IReadOnlyList<string>? args, string sender, CancellationToken ct, bool checkGameCooldown = true, int minimumTokenCost = 0)
        {
            if (checkGameCooldown && !await RequireGameCommandCooldownReady(sender, ct).ConfigureAwait(false))
                return null;
            if (!await RequireTokenBalance(sender, minimumTokenCost, ct).ConfigureAwait(false))
                return null;
            return await ResolveTargetAt(args, 0, sender, ct).ConfigureAwait(false);
        }
        static string GetSingleTargetMinecraftName(ResolvedTarget target)
        {
            if (MinecraftNameHelper.TryNormalizePlayerName(target.MinecraftName, out string playerName))
                return playerName;
            return MinecraftNameHelper.TryNormalizePlayerName(target.DisplayName, out playerName) ? playerName : string.Empty;
        }
        async Task<bool> ValidateEffectCount(int count, string sender, CancellationToken ct)
        {
            if (count <= 0)
            {
                await SayToChannel(sender + ", effect count must be at least 1.", ct).ConfigureAwait(false);
                return false;
            }
            if (count > MaxEffectCount)
            {
                await SayToChannel(sender + ", effect count cannot be higher than " + MaxEffectCount.ToString(CultureInfo.InvariantCulture) + ".", ct).ConfigureAwait(false);
                return false;
            }
            return true;
        }
        static string GetChannelTargetName(BotMainHandler runtime, ResolvedTarget? target)
        {
            if (target == null)
                return "everyone";
            if (!runtime.MultiTargetingEnabled && target.PlayerCount == 1 && !string.IsNullOrWhiteSpace(runtime.StreamerName))
                return runtime.StreamerName;
            string displayName = (target.DisplayName ?? string.Empty).Trim();
            if (displayName.Length > 0 && !string.Equals(displayName, "everyone", StringComparison.OrdinalIgnoreCase))
                return displayName;
            string minecraftName = (target.MinecraftName ?? string.Empty).Trim();
            if (minecraftName.Length > 0)
                return minecraftName;
            return displayName.Length == 0 ? "everyone" : displayName;
        }
        string TargetName(ResolvedTarget target) => GetChannelTargetName(runtime, target);
        async Task TellOthers(ResolvedTarget? target, string message, string color, bool bold, CancellationToken ct)
        {
            if (target is null || !runtime.MultiTargetingEnabled || target.PlayerCount != 1)
                return;
            string targetName = (target.DisplayName ?? string.Empty).Trim();
            if (targetName.Length == 0)
                return;
            await runtime.SendTellrawToOthersAsync(target, targetName.ToUpperInvariant() + " " + message, color, bold, ct).ConfigureAwait(false);
        }
        async Task<bool> RequireAllowed(string sender, string commandName, CancellationToken ct)
        {
            if (runtime.IsAllowedUser(sender))
                return true;
            await SayToChannel((string.IsNullOrWhiteSpace(sender) ? "This user" : sender) + ", only the broadcaster (or moderators if allowed) can use !" + commandName + ".", ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> RequireMinecraftReady(string sender, CancellationToken ct)
        {
            if (runtime.MinecraftServerReady)
                return true;
            await SayToChannel(sender + ", the Minecraft server is still starting. Try again in a moment.", ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> RequireGameCommandCooldownReady(string sender, CancellationToken ct)
        {
            if (!await RequireMinecraftReady(sender, ct).ConfigureAwait(false))
                return false;
            if (!runtime.GlobalGameCommandCooldownEnabled || !runtime.TryGetGlobalGameCommandCooldownRemaining(out TimeSpan remaining))
                return true;
            int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            await SayToChannel(
                sender + ", game commands are on global cooldown. Try again in " + seconds.ToString(CultureInfo.InvariantCulture) + "s.",
                ct).ConfigureAwait(false);
            return false;
        }
        async Task<long?> TryReserveGameCommandCooldown(string sender, CancellationToken ct)
        {
            if (!await RequireMinecraftReady(sender, ct).ConfigureAwait(false))
                return null;
            if (runtime.TryReserveGlobalGameCommandCooldown(out TimeSpan remaining, out long reservationTicks))
                return reservationTicks;
            int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            await SayToChannel(
                sender + ", game commands are on global cooldown. Try again in " + seconds.ToString(CultureInfo.InvariantCulture) + "s.",
                ct).ConfigureAwait(false);
            return null;
        }
        async Task<bool> SendTargetedPricedCommand(ResolvedTarget target, string sender, int baseCost, Func<ResolvedTarget, IEnumerable<string>> buildCommands, CancellationToken ct, string? targetMessage = null, string? othersMessage = null, string color = "yellow", bool bold = true, string? othersColor = null)
        {
            int cost = runtime.ScaleCost(baseCost, target.PlayerCount);
            if (!await TrySendPricedCommands(sender, cost, () => buildCommands(target), ct).ConfigureAwait(false))
                return false;
            if (!string.IsNullOrWhiteSpace(targetMessage))
                await runtime.SendTellrawAsync(target.Selector, targetMessage, color, bold, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(othersMessage))
                await TellOthers(target, othersMessage, othersColor ?? color, bold, ct).ConfigureAwait(false);
            return true;
        }
        async Task<bool> SendSingleTargetedPricedCommand(ResolvedTarget target, string sender, int baseCost, string command, CancellationToken ct, string? targetMessage = null, string? othersMessage = null, string color = "yellow", bool bold = true, string? othersColor = null)
        {
            int cost = runtime.ScaleCost(baseCost, target.PlayerCount);
            if (!await TrySendPricedCommand(sender, cost, command, ct).ConfigureAwait(false))
                return false;
            if (!string.IsNullOrWhiteSpace(targetMessage))
                await runtime.SendTellrawAsync(target.Selector, targetMessage, color, bold, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(othersMessage))
                await TellOthers(target, othersMessage, othersColor ?? color, bold, ct).ConfigureAwait(false);
            return true;
        }
        async Task SendTargetedPricedCommandAndSay(
            ResolvedTarget target,
            string sender,
            int baseCost,
            Func<ResolvedTarget, IEnumerable<string>> buildCommands,
            CancellationToken ct,
            string? targetMessage,
            string? othersMessage,
            string channelMessage,
            string color = "yellow",
            bool bold = true,
            string? othersColor = null)
        {
            if (await SendTargetedPricedCommand(target, sender, baseCost, buildCommands, ct, targetMessage, othersMessage, color, bold, othersColor).ConfigureAwait(false))
                await SayToChannel(channelMessage, ct).ConfigureAwait(false);
        }
        async Task SendSingleTargetedPricedCommandAndSay(
            ResolvedTarget target,
            string sender,
            int baseCost,
            string command,
            CancellationToken ct,
            string? targetMessage,
            string? othersMessage,
            string channelMessage,
            string color = "yellow",
            bool bold = true,
            string? othersColor = null)
        {
            if (await SendSingleTargetedPricedCommand(target, sender, baseCost, command, ct, targetMessage, othersMessage, color, bold, othersColor).ConfigureAwait(false))
                await SayToChannel(channelMessage, ct).ConfigureAwait(false);
        }
        void AddTargetedCommand(string commandName, Func<ResolvedTarget, string, CancellationToken, Task> execute, ChatCommandStatisticFlags commandStatisticFlags = ChatCommandStatisticFlags.None, bool checkGameCooldown = true, int minimumTokenCost = 0)
        {
            AddCommand(commandName, async (args, sender, ct) =>
            {
                ResolvedTarget? target = await PrepareTargetedCommand(args, sender, ct, checkGameCooldown, minimumTokenCost).ConfigureAwait(false);
                if (target != null)
                    await execute(target, sender, ct).ConfigureAwait(false);
            }, commandStatisticFlags);
        }
        void AddSimpleTargetedCommands(params SimpleTargetedCommandRegistration[] definitions)
        {
            foreach (SimpleTargetedCommandRegistration definition in definitions)
            {
                AddTargetedCommand(definition.Name, (target, sender, ct) =>
                    SendTargetedPricedCommandAndSay(
                        target,
                        sender,
                        definition.BaseCost,
                        definition.BuildCommands,
                        ct,
                        definition.BuildTargetMessage(sender, target),
                        definition.OthersMessage,
                        definition.BuildChannelMessage(sender, target),
                        definition.Color,
                        definition.Bold,
                        definition.OthersColor),
                    commandStatisticFlags: definition.StatisticFlags,
                    minimumTokenCost: definition.BaseCost);
            }
        }
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
                    await SayToChannel(sender + ", you gave " + effectPretty + " to " + channelTargetName + ".", ct).ConfigureAwait(false);
            }
            if (count > 1)
                await SayToChannel(sender + ", you gave " + count.ToString(CultureInfo.InvariantCulture) + " effects to " + channelTargetName + ".", ct).ConfigureAwait(false);
        }
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
            string color = InsultTitleColors[BotMainHandler.Randomizer.Next(InsultTitleColors.Length)];
            await SendTargetedPricedCommandAndSay(
                target,
                sender,
                5,
                _ =>
                [
                    MinecraftCommandBuilder.TitleTimes(target.Selector, 0, 400, 10),
                    MinecraftCommandBuilder.Title(target.Selector, "Wow, you suck!", color, runtime.UsesInlineTextComponentSyntax)
                ],
                ct,
                sender + " insulted you!",
                "GOT INSULTED!",
                sender + ", you insulted " + TargetName(target) + "...");
        }
        async Task HandleJohnny(ResolvedTarget target, string sender, CancellationToken ct)
        {
            List<string> commands = MinecraftCommandFeatureBuilder.BuildJohnnyCommands(target.Selector, BotMainHandler.Randomizer, runtime.UsesInlineTextComponentSyntax, runtime.UsesModernEntityAttributeNbt);
            commands.Add(MinecraftCommandBuilder.TitleTimes(target.Selector, 0, 100, 10));
            commands.Add(MinecraftCommandBuilder.Title(target.Selector, " ", "white", runtime.UsesInlineTextComponentSyntax));
            commands.Add(MinecraftCommandBuilder.Subtitle(target.Selector, "Johnny is coming!", "red", runtime.UsesInlineTextComponentSyntax));
            await SendTargetedPricedCommandAndSay(
                target,
                sender,
                40,
                _ => commands,
                ct,
                sender + " sent Johnny after you.",
                "JOHNNY IS COMING!",
                sender + ", you spawned Johnny for " + TargetName(target) + ".",
                othersColor: "red");
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
            int times = BotMainHandler.Randomizer.Next(3, 5);
            var commands = new List<string>(times);
            for (int i = 0; i < times; i++)
            {
                double offsetX = (BotMainHandler.Randomizer.NextDouble() * 2.0) - 1.0;
                double offsetZ = (BotMainHandler.Randomizer.NextDouble() * 2.0) - 1.0;
                commands.Add(MinecraftCommandBuilder.Loot(target.Selector, runtime.GetRandomLootTable(), offsetX, offsetZ));
            }
            await SendTargetedPricedCommandAndSay(
                target,
                sender,
                5,
                _ => commands,
                ct,
                sender + " gave you a pile of loot.",
                "GOT SOME LOOT!",
                sender + ", you gave " + TargetName(target) + " a pile of loot.");
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
                ct,
                sender + " summoned a " + pretty + " on you.",
                "GOT A MOB SPAWNED ON THEM!",
                sender + ", you summoned a " + pretty + " on " + TargetName(target) + ".");
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
                ct,
                targetMessage: null,
                othersMessage: "GOT SWARMED!",
                channelMessage: sender + ", you spawned " + string.Join(", ", prettyNames) + " on " + TargetName(target) + ".");
        }
        async Task HandleSwitchMilk(ResolvedTarget target, string sender, CancellationToken ct)
        {
            (string itemID, string itemName) = BotMainHandler.Randomizer.Next(100) switch
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
                ct,
                targetMessage: null,
                othersMessage: null,
                channelMessage: sender + ", you changed " + TargetName(target) + "'s milk bucket into " + itemName + " (if they had one).");
        }
        async Task HandleWeather(string[]? _, string sender, CancellationToken ct)
        {
            bool thunder = BotMainHandler.Randomizer.Next(2) == 0;
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
                ct,
                sender + " sent you into the sky!",
                "GOT SENT INTO THE SKY!",
                sender + ", you sent " + TargetName(target) + " into the sky.");
        Task HandleScared(ResolvedTarget target, string sender, CancellationToken ct)
            => SendTargetedPricedCommandAndSay(
                target,
                sender,
                15,
                _ => MinecraftCommandFeatureBuilder.BuildScaredCommands(target.Selector, BotMainHandler.Randomizer, runtime.UsesInlineTextComponentSyntax),
                ct,
                sender + " thinks you're a scaredy cat and spawned cats above you.",
                "GOT BURIED IN CATS!",
                sender + ", you spawned 20 cats on " + TargetName(target) + ".");
        Task HandleSlaughter(ResolvedTarget target, string sender, CancellationToken ct)
            => SendTargetedPricedCommandAndSay(
                target,
                sender,
                30,
                _ => MinecraftCommandFeatureBuilder.BuildSlaughterCommands(target.Selector, runtime.MobLootGameRuleName),
                ct,
                sender + " slaughtered any nearby mobs.",
                "GOT THEIR AREA SLAUGHTERED!",
                sender + ", you slaughtered any nearby mobs around " + TargetName(target) + ".");
        AddCommand("ban", HandleBan);
        AddTokenHandlers(runtime, handlers, SayToChannel, RequireAllowed);
        AddCommand("help", HandleHelp);
        AddCommand("playerlist", HandlePlayerList);
        AddCommand("unban", HandleUnban);
        AddSimpleTargetedCommands(
            new("anvil", 5, target =>
            [
                MinecraftCommandBuilder.ClearVerticalColumn(target.Selector, 5),
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
            new("totem", 100, target => [$"item replace entity {target.Selector} weapon.offhand with minecraft:totem_of_undying"], (sender, _) => sender + " gave you a Totem of Undying!", "GOT A TOTEM!", (sender, target) => sender + ", you gave " + TargetName(target) + " a Totem of Undying.", StatisticFlags: NiceCommand),
            new("troll", 5, target => [$"execute as {target.Selector} at @s run playsound minecraft:entity.creeper.primed master @s ~ ~ ~ 1 1"], (_, _) => null, null, (sender, target) => sender + ", you played a creeper noise on " + TargetName(target) + ".", StatisticFlags: DangerousCommand),
            new("water", 15, target => [$"execute at {target.Selector} run setblock ~ ~3 ~ minecraft:water"], (sender, _) => sender + " released water above you.", "GOT WATER RELEASED ON THEM!", (sender, target) => $"{sender}, you released water above {TargetName(target)}.", StatisticFlags: GameCommand),
            new("xp", 5, target => [$"experience add {target.Selector} -1 levels"], (sender, _) => sender + " took away 1 of your XP levels.", "LOST 1 XP LEVEL!", (sender, target) => sender + ", you removed 1 XP level from " + TargetName(target) + ".", StatisticFlags: DangerousCommand));
        AddCommand("effect", HandleEffect, GameCommand);
        AddTargetedCommand("fireworks", HandleFireworks, GameCommand, minimumTokenCost: 10);
        AddTargetedCommand("insult", HandleInsult, DangerousCommand, minimumTokenCost: 5);
        AddTargetedCommand("johnny", HandleJohnny, DangerousCommand, minimumTokenCost: 40);
        AddCommand("lightning", HandleLightning, DangerousCommand);
        AddTargetedCommand("loot", HandleLoot, NiceCommand, minimumTokenCost: 5);
        AddTargetedCommand("mlg", HandleMlg, DangerousCommand, minimumTokenCost: 150);
        AddTargetedCommand("mob", HandleMob, DangerousCommand, minimumTokenCost: 10);
        AddCommand("night", HandleNight, DangerousCommand);
        AddCommand("rename", HandleRename, GameCommand);
        AddTargetedCommand("scared", HandleScared, DangerousCommand, minimumTokenCost: 15);
        AddTargetedCommand("slaughter", HandleSlaughter, DangerousCommand, minimumTokenCost: 30);
        AddTargetedCommand("swarm", HandleSwarm, DangerousCommand, minimumTokenCost: 45);
        AddTargetedCommand("switchmilk", HandleSwitchMilk, DangerousCommand, minimumTokenCost: 6);
        AddCommand("weather", HandleWeather, DangerousCommand);
        MinigameManager.AddMinigameHandlers(runtime, handlers, SayToChannel);
        return handlers;
    }
    private sealed record SimpleTargetedCommandRegistration(
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
    private static void AddTokenHandlers(
        BotMainHandler runtime,
        Dictionary<string, ChatCommandHandler> handlers,
        Func<string?, CancellationToken, Task> sayToChannel,
        Func<string, string, CancellationToken, Task<bool>> requireAllowed)
    {
        handlers["gambletokens"] = async (args, sender, ct) =>
        {
            string who = NormalizeCommandUser(sender);
            if (args is null || args.Length < 1)
            {
                await sayToChannel(who + ", usage: !gambletokens amount risk (1-10) — example: !gambletokens 20 5", ct).ConfigureAwait(false);
                return;
            }
            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 5)
            {
                await sayToChannel(who + ", invalid amount. Minimum gamble amount is 5 tokens.", ct).ConfigureAwait(false);
                return;
            }
            if (amount > 150)
            {
                await sayToChannel(who + ", maximum gamble amount is 150 tokens per bet.", ct).ConfigureAwait(false);
                return;
            }
            int risk = 5;
            if (args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRisk))
                risk = parsedRisk;
            risk = Math.Clamp(risk, 1, 10);
            if (runtime.IsGambleOnCooldown(who, out TimeSpan cooldownRemaining))
            {
                await sayToChannel(string.Format(CultureInfo.InvariantCulture, "{0}, gamble is on cooldown. Try again in {1}.", who, FormatMinutesSeconds(cooldownRemaining)), ct).ConfigureAwait(false);
                return;
            }
            int balance = runtime.GetTokens(who);
            if (!runtime.TrySpendTokens(who, amount))
            {
                await sayToChannel(string.Format(CultureInfo.InvariantCulture, "{0}, you must have at least {1} tokens to gamble that amount. You currently have {2}.", who, amount, balance), ct).ConfigureAwait(false);
                return;
            }
            double winChance = 0.9 - ((risk - 1) * 0.08888888888888889);
            double payoutMul = 1.05 + ((risk - 1) * 0.21666666666666667);
            runtime.StartGambleCooldown(who, GambleTokenCooldown);
            bool win = BotMainHandler.Randomizer.NextDouble() < winChance;
            if (win)
            {
                int gain = (int)Math.Round(amount * (payoutMul - 1.0));
                if (gain <= 0)
                    gain = 1;
                runtime.AdjustTokens(who, amount + gain); // Gamble win payout restores bet plus profit.
                int newBalance = runtime.GetTokens(who);
                await sayToChannel(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, you gambled {1} {2} at risk {3} and WON! You gained {4} {5} and now have {6} tokens total.",
                        who, amount, CommandTokenWord(amount), risk, gain, CommandTokenWord(gain), newBalance),
                    ct).ConfigureAwait(false);
            }
            else
            {
                int newBalance = runtime.GetTokens(who);
                await sayToChannel(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, you gambled {1} {2} at risk {3} and LOST. You lost {4} {5} and now have {6} tokens total.",
                        who, amount, CommandTokenWord(amount), risk, amount, CommandTokenWord(amount), newBalance),
                    ct).ConfigureAwait(false);
            }
        };
        handlers["givetokens"] = (args, sender, ct) =>
            HandleTokenAdjustmentCommandAsync(args, sender, "givetokens", isGive: true, ct);
        handlers["removetokens"] = (args, sender, ct) =>
            HandleTokenAdjustmentCommandAsync(args, sender, "removetokens", isGive: false, ct);
        async Task HandleTokenAdjustmentCommandAsync(string[]? args, string sender, string commandName, bool isGive, CancellationToken ct)
        {
            string verb = isGive ? "gave" : "removed";
            string action = isGive ? "give" : "remove";
            string direction = isGive ? "to" : "from";
            string usage = "Usage: !" + commandName + " [username|all|random] amount";
            string who = NormalizeCommandUser(sender);
            if (!await requireAllowed(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (args is null || args.Length < 1)
            {
                await sayToChannel(usage, ct).ConfigureAwait(false);
                return;
            }
            string targetToken = args.Length == 1 ? who : (args[0] ?? string.Empty).Trim().TrimStart('@');
            string amountToken = args.Length == 1 ? args[0] ?? string.Empty : args[1] ?? string.Empty;
            if (!int.TryParse(amountToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            {
                await sayToChannel("Invalid amount. " + usage, ct).ConfigureAwait(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(targetToken))
                targetToken = who;
            int delta = isGive ? amount : -amount;
            if (targetToken.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                List<string> chatters = runtime.GetKnownChattersSnapshot();
                if (chatters.Count == 0)
                {
                    await sayToChannel(who + ", there are no known viewers to " + action + " tokens " + direction + " right now.", ct).ConfigureAwait(false);
                    return;
                }
                runtime.AdjustTokens(chatters, delta);
                await sayToChannel(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} all tracked viewers ({5}).", who, verb, amount, CommandTokenWord(amount), direction, chatters.Count), ct).ConfigureAwait(false);
                return;
            }
            if (targetToken.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                List<string> chatters = runtime.GetKnownChattersSnapshot();
                if (chatters.Count == 0)
                {
                    await sayToChannel(who + ", there are no known viewers to choose from right now.", ct).ConfigureAwait(false);
                    return;
                }
                string chosen = chatters[BotMainHandler.Randomizer.Next(chatters.Count)];
                runtime.AdjustTokens(chosen, delta);
                await sayToChannel(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} random viewer {5}.", who, verb, amount, CommandTokenWord(amount), direction, chosen), ct).ConfigureAwait(false);
                return;
            }
            if (!CommandUserHelper.TryNormalizeTwitchUsername(targetToken, out string targetUsername))
            {
                await sayToChannel(who + ", please provide a valid Twitch username to " + action + " tokens " + direction + ".", ct).ConfigureAwait(false);
                return;
            }
            runtime.AdjustTokens(targetUsername, delta);
            await sayToChannel(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5}.", who, verb, amount, CommandTokenWord(amount), direction, targetUsername), ct).ConfigureAwait(false);
        }
        handlers["tokens"] = async (args, sender, ct) =>
        {
            string whoAsked = NormalizeCommandUser(sender);
            string queryUser = whoAsked;
            if (args is { Length: > 0 } && !CommandUserHelper.TryNormalizeTwitchUsername(args[0], out queryUser))
            {
                await sayToChannel(whoAsked + ", please provide a valid Twitch username to check tokens for.", ct).ConfigureAwait(false);
                return;
            }
            int balance = runtime.GetTokens(queryUser);
            if (args is { Length: > 0 })
                await sayToChannel(whoAsked + ", " + queryUser + " has " + balance.ToString(CultureInfo.InvariantCulture) + " " + CommandTokenWord(balance) + ".", ct).ConfigureAwait(false);
            else
                await sayToChannel(whoAsked + ", you have " + balance.ToString(CultureInfo.InvariantCulture) + " " + CommandTokenWord(balance) + ".", ct).ConfigureAwait(false);
        };
        handlers["tradetokens"] = async (args, sender, ct) =>
        {
            if (args is null || args.Length < 2)
            {
                await sayToChannel("Usage: !tradetokens username amount", ct).ConfigureAwait(false);
                return;
            }
            string rawToUser = (args[0] ?? string.Empty).Trim().Trim('@');
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            {
                await sayToChannel("Invalid amount. Usage: !tradetokens username amount", ct).ConfigureAwait(false);
                return;
            }
            string fromUser = NormalizeCommandUser(sender);
            if (!CommandUserHelper.TryNormalizeTwitchUsername(rawToUser, out string toUser))
            {
                await sayToChannel(fromUser + ", please provide a valid Twitch username to trade tokens to.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(toUser, fromUser, StringComparison.OrdinalIgnoreCase))
            {
                await sayToChannel(fromUser + ", you cannot trade tokens to yourself.", ct).ConfigureAwait(false);
                return;
            }
            if (!runtime.TrySpendTokens(fromUser, amount))
            {
                await sayToChannel(fromUser + ", you don't have enough tokens to trade.", ct).ConfigureAwait(false);
                return;
            }
            int received = amount / 2;
            if (received > 0)
                runtime.AdjustTokens(toUser, received);
            await sayToChannel(string.Format(CultureInfo.InvariantCulture, "{0} traded {1} tokens to {2}. {2} received {3} tokens (50%).", fromUser, amount, toUser, received), ct).ConfigureAwait(false);
        };
    }
    private static string NormalizeCommandUser(string? value) => CommandUserHelper.NormalizeUsername(value);
    private static string CommandTokenWord(int amount) => amount == 1 ? "token" : "tokens";
    private static string PrettyMinecraftName(string id)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((id ?? string.Empty).Replace('_', ' '));
    private static string FormatMinutesSeconds(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        int minutes = seconds / 60;
        int leftover = seconds % 60;
        return minutes > 0
            ? minutes.ToString(CultureInfo.InvariantCulture) + "m " + leftover.ToString(CultureInfo.InvariantCulture) + "s"
            : seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }
}
