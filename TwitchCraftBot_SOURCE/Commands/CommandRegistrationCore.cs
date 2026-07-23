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
        private static readonly Task<long?> NoCooldownReservationTask = Task.FromResult<long?>(0);

        private static Task<long?> ReserveNoCooldownAsync(CancellationToken _) => NoCooldownReservationTask;

        private static void ReleaseNoCooldown(long _)
        {
        }

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
        async Task<bool> RequireTokenBalance(string sender, int cost, CancellationToken ct)
        {
            if (cost <= 0 || runtime.GetTokens(sender) >= cost) return true;
            await SayInsufficientTokens(sender, cost, ct).ConfigureAwait(false);
            return false;
        }
        Task<bool> ExecutePaidCommandTransaction(
            string sender,
            int cost,
            Func<CancellationToken, Task<bool>> dispatchAsync,
            Func<CancellationToken, Task<long?>> reserveCooldownAsync,
            Action<long> releaseCooldown,
            CancellationToken ct,
            Action? onSendFailure = null)
        {
            PaidCommandTransactionDependencies dependencies = new()
            {
                ReserveCooldownAsync = reserveCooldownAsync,
                ReleaseCooldown = releaseCooldown,
                TrySpendTokens = amount => runtime.TrySpendTokens(sender, amount),
                RefundTokens = amount => runtime.AdjustTokens(sender, amount),
                DispatchAsync = dispatchAsync,
                RecordStatistics = amount => runtime.RecordCurrentGameAffectingCommandForStatistics(sender, amount),
                ReportInsufficientTokensAsync = (amount, token) => SayInsufficientTokens(sender, amount, token),
                ReportDispatchFailureAsync = token => SayToChannel(
                    sender + ", the Minecraft command could not be sent, so your tokens were refunded.",
                    token),
                NotifyFailure = onSendFailure
            };

            return PaidCommandTransaction.ExecuteAsync(dependencies, cost, ct);
        }

        Task<bool> TrySendPaidCommandsWithoutGameCooldown(string sender, int cost, Func<IEnumerable<string>> buildCommands, CancellationToken ct, Action? onSendFailure = null)
        {
            return ExecutePaidCommandTransaction(
                sender,
                cost,
                token => runtime.SendServerCommandsAsync(buildCommands(), token),
                ReserveNoCooldownAsync,
                ReleaseNoCooldown,
                ct,
                onSendFailure);
        }

        Task<bool> TrySendPaidCommandWithoutGameCooldown(string sender, int cost, string command, CancellationToken ct, Action? onSendFailure = null)
        {
            return ExecutePaidCommandTransaction(
                sender,
                cost,
                token => runtime.SendServerCommandAsync(command, token),
                ReserveNoCooldownAsync,
                ReleaseNoCooldown,
                ct,
                onSendFailure);
        }

        Task<bool> TrySendPricedCommands(string sender, int cost, Func<IEnumerable<string>> buildCommands, CancellationToken ct)
        {
            return ExecutePaidCommandTransaction(
                sender,
                cost,
                token => runtime.SendServerCommandsAsync(buildCommands(), token),
                token => TryReserveGameCommandCooldown(sender, token),
                runtime.ClearGlobalGameCommandCooldown,
                ct);
        }

        Task<bool> TrySendPricedCommand(string sender, int cost, string command, CancellationToken ct)
        {
            return ExecutePaidCommandTransaction(
                sender,
                cost,
                token => runtime.SendServerCommandAsync(command, token),
                token => TryReserveGameCommandCooldown(sender, token),
                runtime.ClearGlobalGameCommandCooldown,
                ct);
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
    }
}
