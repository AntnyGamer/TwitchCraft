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
        static List<string> NormalizeTargets(List<string>? players)
            => SortedListHelper.NormalizePlayerNames(players, StringComparer.OrdinalIgnoreCase);
        Task SayAsync(string? msg, CancellationToken ct)
            => ReplyAsync(msg, BotResponseKind.Essential, ct);
        Task ConfirmAsync(string? msg, CancellationToken ct)
        {
            runtime.MarkCommandSuccess();
            return ReplyAsync(msg, BotResponseKind.Confirmation, ct);
        }
        Task SuccessAsync(string? msg, CancellationToken ct)
        {
            runtime.MarkCommandSuccess();
            return ReplyAsync(msg, BotResponseKind.Essential, ct);
        }
        Task ReplyAsync(string? msg, BotResponseKind kind, CancellationToken ct)
            => string.IsNullOrWhiteSpace(msg) ? Task.CompletedTask : runtime.SendReplyAsync(msg, kind, ct);
        Task SayNotEnoughTokensAsync(string sender, int cost, CancellationToken ct)
            => SayAsync(sender + ", you need at least " + cost.ToString(CultureInfo.InvariantCulture) + " tokens for this command.", ct);
        async Task<bool> RequireAdminAsync(string sender, string commandName, CancellationToken ct)
        {
            if (runtime.RemoteControlEnabled || !runtime.MultiplayerEnabled)
            {
                string mode = runtime.RemoteControlEnabled ? "Remote Control Mode" : "singleplayer mode";
                await SayAsync(sender + ", !" + commandName + " is not available in " + mode + ".", ct).ConfigureAwait(false);
                return false;
            }
            if (runtime.MinecraftServerReady)
                return true;
            await SayAsync(sender + ", the Minecraft server is still starting. Try again in a moment.", ct).ConfigureAwait(false);
            return false;
        }
        static string GetArg(string[]? args, int index)
        {
            if (args is null || index < 0 || index >= args.Length)
                return string.Empty;
            return args[index] ?? string.Empty;
        }
        async Task<bool> RequireTokensAsync(string sender, int cost, CancellationToken ct)
        {
            int scaledCost = runtime.Commands.ScaleCost(cost, 1);
            if (scaledCost <= 0 || runtime.Tokens.GetBalance(sender) >= scaledCost) return true;
            await SayNotEnoughTokensAsync(sender, scaledCost, ct).ConfigureAwait(false);
            return false;
        }
        Task<bool> RunPaidCommandAsync(
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
                TrySpendTokens = amount => runtime.Tokens.TrySpend(sender, amount),
                RefundTokens = amount => runtime.Tokens.Adjust(sender, amount),
                DispatchAsync = dispatchAsync,
                RecordStatistics = amount =>
                {
                    runtime.MarkCommandSuccess();
                    runtime.Statistics.RecordCommand(sender, amount);
                },
                ReportInsufficientTokensAsync = (amount, token) => SayNotEnoughTokensAsync(sender, amount, token),
                ReportDispatchFailureAsync = token => SayAsync(
                    sender + ", the Minecraft command could not be sent, so your tokens were refunded.",
                    token),
                NotifyFailure = onSendFailure
            };

            return PaidCommandTransaction.ExecuteAsync(dependencies, cost, ct);
        }

        Task<bool> TrySendPaidNoCooldownAsync(string sender, int cost, string command, CancellationToken ct, Action? onSendFailure = null)
        {
            return RunPaidCommandAsync(
                sender,
                cost,
                token => runtime.SendServerCommandAsync(command, token),
                ReserveNoCooldownAsync,
                ReleaseNoCooldown,
                ct,
                onSendFailure);
        }

        Task<bool> TrySendPricedAsync(string sender, int cost, Func<IEnumerable<string>> buildCommands, CancellationToken ct)
        {
            return RunPaidCommandAsync(
                sender,
                cost,
                token => runtime.SendServerCommandsAsync(buildCommands(), token),
                token => TryReserveCooldownAsync(sender, token),
                runtime.Commands.ClearGlobalCooldown,
                ct);
        }

        Task<bool> TrySendPricedAsync(string sender, int cost, string command, CancellationToken ct)
        {
            return RunPaidCommandAsync(
                sender,
                cost,
                token => runtime.SendServerCommandAsync(command, token),
                token => TryReserveCooldownAsync(sender, token),
                runtime.Commands.ClearGlobalCooldown,
                ct);
        }
        static bool IsEveryone(ResolvedTarget? target)
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
        static bool HasPlayer(IReadOnlyList<string> players, string playerName)
            => SortedListHelper.Contains(players, playerName, StringComparer.OrdinalIgnoreCase);
        bool IsSingleplayer() => !runtime.MultiTargetingEnabled;
        async Task<ResolvedTarget?> FilterSpectatorsAsync(ResolvedTarget? target, CancellationToken ct, List<string>? cachedTargetablePlayers = null)
        {
            if (target == null)
                return null;
            List<string> activePlayers = cachedTargetablePlayers ??
                NormalizeTargets(await runtime.GetPlayersAsync(ct).ConfigureAwait(false));
            string defaultMinecraftPlayer = runtime.DefaultMinecraftPlayerName;
            bool defaultPlayerIsValid = defaultMinecraftPlayer.Length > 0;
            if (IsSingleplayer() && activePlayers.Count == 0 && defaultPlayerIsValid)
                activePlayers = [defaultMinecraftPlayer];
            int activeCount = activePlayers.Count;
            bool activePlayersIncludeDefault = defaultPlayerIsValid
                && HasPlayer(activePlayers, defaultMinecraftPlayer);
            bool everyone = IsEveryone(target);
            target.TargetablePlayers = activePlayers;
            if (everyone)
            {
                target.Selector = "@a[gamemode=!spectator]";
                target.PlayerCount = activeCount;
                target.DefaultPlayerInclusionKnown = true;
                target.IncludesDefaultMinecraftPlayer = activePlayersIncludeDefault || (IsSingleplayer() && defaultPlayerIsValid);
                if (IsSingleplayer() && !string.IsNullOrWhiteSpace(runtime.StreamerName))
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
                bool targetIsActive = activeCount > 0 && HasPlayer(activePlayers, targetName);
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
                target.IncludesDefaultMinecraftPlayer = activePlayersIncludeDefault || (IsSingleplayer() && defaultPlayerIsValid);
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
        async Task<bool> IncludesStreamerAsync(ResolvedTarget target, CancellationToken ct)
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
            List<string> targetablePlayers = NormalizeTargets(target.TargetablePlayers ?? await runtime.GetPlayersAsync(ct).ConfigureAwait(false));
            return HasPlayer(targetablePlayers, streamerMinecraftName);
        }
        async Task<ResolvedTarget?> ResolveTargetAsync(IReadOnlyList<string>? args, int startIndex, string sender, CancellationToken ct)
        {
            if (args is not null && startIndex >= 0 && startIndex < args.Count)
            {
                string first = (args[startIndex] ?? string.Empty).Trim();
                if (first.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    if (!runtime.AllowRandomPlayerTarget)
                    {
                        await SayAsync(sender + ", random player targeting is disabled.", ct).ConfigureAwait(false);
                        return null;
                    }

                    List<string> players = NormalizeTargets(await runtime.GetPlayersAsync(ct).ConfigureAwait(false));
                    string defaultPlayer = runtime.DefaultMinecraftPlayerName;
                    if (players.Count == 0 &&
                        IsSingleplayer() &&
                        defaultPlayer.Length > 0)
                    {
                        players = [defaultPlayer];
                    }
                    if (players.Count == 0)
                    {
                        await SayAsync(sender + ", no players are online to target right now.", ct).ConfigureAwait(false);
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
                    randomTarget = await FilterSpectatorsAsync(randomTarget, ct, players).ConfigureAwait(false);
                    if (randomTarget is null || randomTarget.PlayerCount <= 0)
                    {
                        await SayAsync(sender + ", no players can be targeted right now.", ct).ConfigureAwait(false);
                        return null;
                    }
                    if (IsSingleplayer() && !string.IsNullOrEmpty(runtime.StreamerName))
                        randomTarget.DisplayName = runtime.StreamerName;
                    return randomTarget;
                }
            }
            ResolvedTarget? resolved = await runtime.Commands.ResolveTargetAsync(
                args,
                startIndex,
                sender,
                SayAsync,
                ct).ConfigureAwait(false);
            ResolvedTarget? target = await FilterSpectatorsAsync(resolved, ct).ConfigureAwait(false);
            if (target == null)
                return null;
            if (target.PlayerCount <= 0)
            {
                string failure = IsEveryone(target)
                    ? sender + ", no players can be targeted right now."
                    : sender + ", that player is spectating or unavailable and cannot be targeted.";
                await SayAsync(failure, ct).ConfigureAwait(false);
                return null;
            }
            if (IsSingleplayer() &&
                !string.IsNullOrEmpty(runtime.StreamerName) &&
                target.PlayerCount == 1 &&
                !IsEveryone(target) &&
                !string.Equals(target.DisplayName, "everyone", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(target.MinecraftName))
                    target.MinecraftName = (target.DisplayName ?? string.Empty).Trim();
                target.DisplayName = runtime.StreamerName;
            }
            return target;
        }
        async Task<ResolvedTarget?> PrepareTargetAsync(IReadOnlyList<string>? args, string sender, CancellationToken ct, bool checkGameCooldown = true, int minimumTokenCost = 0)
        {
            if (checkGameCooldown && !await RequireCooldownAsync(sender, ct).ConfigureAwait(false))
                return null;
            if (!await RequireTokensAsync(sender, minimumTokenCost, ct).ConfigureAwait(false))
                return null;
            return await ResolveTargetAsync(args, 0, sender, ct).ConfigureAwait(false);
        }
        static string GetPlayerName(ResolvedTarget target)
        {
            if (MinecraftNameHelper.TryNormalizePlayerName(target.MinecraftName, out string playerName))
                return playerName;
            return MinecraftNameHelper.TryNormalizePlayerName(target.DisplayName, out playerName) ? playerName : string.Empty;
        }
        async Task<bool> CheckEffectCountAsync(int count, string sender, CancellationToken ct)
        {
            if (count <= 0)
            {
                await SayAsync(sender + ", effect count must be at least 1.", ct).ConfigureAwait(false);
                return false;
            }
            if (count > MaxEffectCount)
            {
                await SayAsync(sender + ", effect count cannot be higher than " + MaxEffectCount.ToString(CultureInfo.InvariantCulture) + ".", ct).ConfigureAwait(false);
                return false;
            }
            return true;
        }
        static string GetChatTarget(BotMainHandler runtime, ResolvedTarget? target)
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
        string TargetName(ResolvedTarget target) => GetChatTarget(runtime, target);
        async Task NotifyOthersAsync(ResolvedTarget? target, string message, string color, bool bold, CancellationToken ct)
        {
            if (target is null || !runtime.MultiTargetingEnabled || target.PlayerCount != 1)
                return;
            string targetName = (target.DisplayName ?? string.Empty).Trim();
            if (targetName.Length == 0)
                return;
            await runtime.TellrawOthersAsync(target, targetName.ToUpperInvariant() + " " + message, color, bold, ct).ConfigureAwait(false);
        }
        async Task<bool> RequirePermissionAsync(string sender, string commandName, CancellationToken ct)
        {
            if (runtime.Commands.IsAllowedUser(sender))
                return true;
            await SayAsync((string.IsNullOrWhiteSpace(sender) ? "This user" : sender) + ", only the broadcaster (or moderators if allowed) can use !" + commandName + ".", ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> RequireMinecraftAsync(string sender, CancellationToken ct)
        {
            if (runtime.MinecraftServerReady)
                return true;
            await SayAsync(sender + ", the Minecraft server is still starting. Try again in a moment.", ct).ConfigureAwait(false);
            return false;
        }
        async Task<bool> RequireCooldownAsync(string sender, CancellationToken ct)
        {
            if (!await RequireMinecraftAsync(sender, ct).ConfigureAwait(false))
                return false;
            if (!runtime.Commands.GlobalGameCommandCooldownEnabled || !runtime.Commands.TryGetGlobalCooldown(out TimeSpan remaining))
                return true;
            await SayAsync(
                sender + ", game commands are on global cooldown. Try again in " + runtime.FormatCooldown(remaining) + ".",
                ct).ConfigureAwait(false);
            return false;
        }
        async Task<long?> TryReserveCooldownAsync(string sender, CancellationToken ct)
        {
            if (!await RequireMinecraftAsync(sender, ct).ConfigureAwait(false))
                return null;
            if (runtime.Commands.TryReserveGlobalCooldown(out TimeSpan remaining, out long reservationTicks))
                return reservationTicks;
            await SayAsync(
                sender + ", game commands are on global cooldown. Try again in " + runtime.FormatCooldown(remaining) + ".",
                ct).ConfigureAwait(false);
            return null;
        }
        async Task<bool> SendPricedAsync(ResolvedTarget target, string sender, int baseCost, Func<ResolvedTarget, IEnumerable<string>> buildCommands, CancellationToken ct, string? targetMessage = null, string? othersMessage = null, string color = DefaultCommandTextColor, bool bold = true, string? othersColor = null)
        {
            int cost = runtime.Commands.ScaleCost(baseCost, target.PlayerCount);
            if (!await TrySendPricedAsync(sender, cost, () => buildCommands(target), ct).ConfigureAwait(false))
                return false;
            if (!string.IsNullOrWhiteSpace(targetMessage))
                await runtime.SendTellrawAsync(target.Selector, targetMessage, color, bold, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(othersMessage))
                await NotifyOthersAsync(target, othersMessage, othersColor ?? color, bold, ct).ConfigureAwait(false);
            return true;
        }
        async Task<bool> SendPricedAsync(ResolvedTarget target, string sender, int baseCost, string command, CancellationToken ct, string? targetMessage = null, string? othersMessage = null, string color = DefaultCommandTextColor, bool bold = true, string? othersColor = null)
        {
            int cost = runtime.Commands.ScaleCost(baseCost, target.PlayerCount);
            if (!await TrySendPricedAsync(sender, cost, command, ct).ConfigureAwait(false))
                return false;
            if (!string.IsNullOrWhiteSpace(targetMessage))
                await runtime.SendTellrawAsync(target.Selector, targetMessage, color, bold, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(othersMessage))
                await NotifyOthersAsync(target, othersMessage, othersColor ?? color, bold, ct).ConfigureAwait(false);
            return true;
        }
        async Task SendPricedReplyAsync(
            ResolvedTarget target,
            string sender,
            int baseCost,
            Func<ResolvedTarget, IEnumerable<string>> buildCommands,
            string? targetMessage,
            string? othersMessage,
            string channelMessage,
            string color,
            bool bold,
            string? othersColor,
            CancellationToken ct)
        {
            if (await SendPricedAsync(target, sender, baseCost, buildCommands, ct, targetMessage, othersMessage, color, bold, othersColor).ConfigureAwait(false))
                await ConfirmAsync(channelMessage, ct).ConfigureAwait(false);
        }
        async Task SendPricedReplyAsync(
            ResolvedTarget target,
            string sender,
            int baseCost,
            string command,
            string? targetMessage,
            string? othersMessage,
            string channelMessage,
            string color,
            bool bold,
            string? othersColor,
            CancellationToken ct)
        {
            if (await SendPricedAsync(target, sender, baseCost, command, ct, targetMessage, othersMessage, color, bold, othersColor).ConfigureAwait(false))
                await ConfirmAsync(channelMessage, ct).ConfigureAwait(false);
        }
        void AddTargetCommand(string commandName, Func<ResolvedTarget, string, CancellationToken, Task> execute, ChatCommandStatisticFlags commandStatisticFlags = ChatCommandStatisticFlags.None, bool checkGameCooldown = true, int minimumTokenCost = 0)
        {
            AddCommand(commandName, async (args, sender, ct) =>
            {
                ResolvedTarget? target = await PrepareTargetAsync(args, sender, ct, checkGameCooldown, minimumTokenCost).ConfigureAwait(false);
                if (target != null)
                    await execute(target, sender, ct).ConfigureAwait(false);
            }, commandStatisticFlags);
        }
        void AddTargetCommands(params TargetedCommandDefinition[] definitions)
        {
            foreach (TargetedCommandDefinition definition in definitions)
            {
                AddTargetCommand(definition.Name, (target, sender, ct) =>
                    SendPricedReplyAsync(
                        target,
                        sender,
                        definition.BaseCost,
                        definition.BuildCommands,
                        definition.BuildTargetMessage(sender, target),
                        definition.OthersMessage,
                        definition.BuildChannelMessage(sender, target),
                        definition.Color,
                        definition.Bold,
                        definition.OthersColor,
                        ct),
                    commandStatisticFlags: definition.StatisticFlags,
                    minimumTokenCost: definition.BaseCost);
            }
        }
    }
}
