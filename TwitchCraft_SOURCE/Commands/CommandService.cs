using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

/// Owns command targeting, authorization, costs, and command-specific cooldown state.
public sealed class CommandService
{
    private const double DefaultGlobalGameCommandCooldownSeconds = 10.0;
    private static readonly long FiveMinuteCommandCooldownTimestampTicks = 5 * 60 * Stopwatch.Frequency;
    private TwitchCraftConfig? _config;
    private string _defaultMinecraftPlayer = string.Empty;
    private string _defaultMinecraftPlayerName = string.Empty;
    private readonly Func<CancellationToken, Task<List<string>>> _refreshPlayers;
    private readonly Lock _cooldownGate = new();
    private readonly Lock _commandStateGate = new();
    private readonly AsyncLocal<string?> _currentCommandSender = new();
    private readonly AsyncLocal<CommandExecutionState?> _currentCommandExecution = new();
    private readonly Queue<long> _channelCommandTimestamps = new();
    private readonly Dictionary<string, Queue<long>> _viewerCommandTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _viewerCommandLimitNotices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Command, string Sender), long> _customCommandCooldownUntilTimestamp = [];
    private readonly Dictionary<string, long> _timedScaleCommandCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _gambleCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private long _lastChannelCommandLimitNoticeTimestamp;
    private long _lastCommandStatePruneTimestamp;
    private long _lastLightningTimestamp = -FiveMinuteCommandCooldownTimestampTicks;
    private int _fireworksRepeatActive;

    internal CommandService(Func<CancellationToken, Task<List<string>>> refreshPlayers)
    {
        ArgumentNullException.ThrowIfNull(refreshPlayers);
        _refreshPlayers = refreshPlayers;
    }

    internal void SetContext(TwitchCraftConfig? config)
    {
        _config = config;
        string configured = config?.Identity.StreamerMinecraftName.Trim() ?? string.Empty;
        string streamer = config?.Twitch.StreamerName.Trim() ?? string.Empty;
        _defaultMinecraftPlayer = configured.Length > 0 ? configured : streamer;
        _defaultMinecraftPlayerName = MinecraftNameHelper.TryNormalizePlayerName(_defaultMinecraftPlayer, out string normalized)
            ? normalized
            : string.Empty;
    }

    internal string DefaultMinecraftPlayer => _defaultMinecraftPlayer;

    internal string DefaultMinecraftPlayerName => _defaultMinecraftPlayerName;

    public bool AllowAllPlayerTarget => _config?.Settings.AllowAllPlayerTarget ?? true;

    public bool AllowRandomPlayerTarget => _config?.Settings.AllowRandomPlayerTarget ?? true;

    public int ScaleCost(int baseCost, int playerCount)
    {
        if (baseCost <= 0)
        {
            return 0;
        }

        TwitchCraftConfig? config = _config;
        bool multiTargetingEnabled = config?.Settings.MultiplayerEnabled == true || config?.Settings.RemoteControlEnabled == true;
        long targetScaledCost = !multiTargetingEnabled || playerCount <= 1
            ? baseCost
            : (baseCost * (playerCount + 1L)) / 2L;
        double multiplier = config?.Settings.CommandCostMultiplier ?? 1.0;
        return GetCommandCost(targetScaledCost, multiplier);
    }

    internal static int GetCommandCost(long cost, double multiplier)
    {
        if (cost <= 0)
            return 0;
        if (!double.IsFinite(multiplier) || multiplier < 0.0 || multiplier > 5.0)
            multiplier = 1.0;
        double scaled = Math.Ceiling(cost * multiplier);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    public bool TryStartFireworks() => Interlocked.Exchange(ref _fireworksRepeatActive, 1) == 0;

    public void StopFireworks() => Volatile.Write(ref _fireworksRepeatActive, 0);

    internal const string GlobalCooldownKey = "\0";

    internal readonly record struct CooldownReservation((string Command, string Sender) Key, long ReservationTimestamp, long CooldownTimestampTicks)
    {
        internal bool IsActive => Key.Command != null;
    }

    private sealed class CommandExecutionState(string name, bool moderator)
    {
        internal string Name { get; } = name;
        internal bool Moderator { get; } = moderator;
        internal bool Succeeded;
    }

    public bool GlobalGameCommandCooldownEnabled
        => _config?.Settings.GlobalGameCommandCooldownEnabled == true && !HasGlobalCooldownOverride();

    internal string CurrentCommandName => _currentCommandExecution.Value?.Name ?? string.Empty;

    internal string CurrentSender => _currentCommandSender.Value ?? string.Empty;

    internal void SetCurrentSender(string? sender) => _currentCommandSender.Value = sender;

    internal void BeginCommand(string commandName, bool isModerator)
        => _currentCommandExecution.Value = new(commandName, isModerator);

    internal void MarkCommandSuccess()
    {
        if (_currentCommandExecution.Value is CommandExecutionState state)
            state.Succeeded = true;
    }

    internal bool CommandSucceeded => _currentCommandExecution.Value?.Succeeded == true;

    internal void EndCommand() => _currentCommandExecution.Value = null;

    internal bool HasPerUserCooldownOverride(string? commandName = null)
        => TryGetCommandSettings(commandName ?? CurrentCommandName, out CommandCustomization customization) &&
            customization.CooldownSeconds.HasValue;

    internal bool HasGlobalCooldownOverride(string? commandName = null)
        => TryGetCommandSettings(commandName ?? CurrentCommandName, out CommandCustomization customization) &&
            customization.GlobalCooldownSeconds.HasValue;

    internal bool TryGetCommandSettings(string? commandName, out CommandCustomization customization)
    {
        customization = null!;
        Dictionary<string, CommandCustomization>? customizations = _config?.Settings.CommandCustomizations;
        if (customizations == null || customizations.Count == 0)
            return false;

        string name = (commandName ?? string.Empty).Trim();
        if (name.Length == 0 || !customizations.TryGetValue(name, out CommandCustomization? found) || found == null)
            return false;

        customization = found;
        return true;
    }

    internal bool TryUseCommandSlots(string viewer, out bool viewerLimited, long? nowTimestamp = null)
    {
        int viewerLimit = _config?.Settings.ViewerCommandLimitPerMinute ?? 0;
        int channelLimit = _config?.Settings.ChannelCommandLimitPerMinute ?? 0;
        viewerLimited = false;
        if (viewerLimit <= 0 && channelLimit <= 0)
            return true;

        long now = nowTimestamp ?? Stopwatch.GetTimestamp(), cutoff = now - 60 * Stopwatch.Frequency;
        lock (_commandStateGate)
        {
            PruneCommandStateNoLock(now);
            Queue<long>? viewerTimestamps = null;
            if (viewerLimit > 0 && viewer.Length > 0 && _viewerCommandTimestamps.TryGetValue(viewer, out viewerTimestamps))
            {
                while (viewerTimestamps.Count > 0 && viewerTimestamps.Peek() <= cutoff)
                    viewerTimestamps.Dequeue();
                if (viewerTimestamps.Count >= viewerLimit)
                {
                    viewerLimited = true;
                    return false;
                }
            }

            if (channelLimit > 0)
            {
                while (_channelCommandTimestamps.Count > 0 && _channelCommandTimestamps.Peek() <= cutoff)
                    _channelCommandTimestamps.Dequeue();
                if (_channelCommandTimestamps.Count >= channelLimit)
                    return false;
            }

            if (viewerLimit > 0 && viewer.Length > 0)
            {
                if (viewerTimestamps == null)
                    _viewerCommandTimestamps[viewer] = viewerTimestamps = new();
                viewerTimestamps.Enqueue(now);
            }
            if (channelLimit > 0)
                _channelCommandTimestamps.Enqueue(now);
            return true;
        }
    }

    internal bool ShouldWarnChannelLimit(long? nowTimestamp = null)
    {
        long now = nowTimestamp ?? Stopwatch.GetTimestamp();
        long previous = Volatile.Read(ref _lastChannelCommandLimitNoticeTimestamp);
        if (previous != 0 && now - previous < 10 * Stopwatch.Frequency)
            return false;
        return Interlocked.CompareExchange(ref _lastChannelCommandLimitNoticeTimestamp, now, previous) == previous;
    }

    internal bool ShouldWarnViewerLimit(string sender, long? nowTimestamp = null)
    {
        string viewer = CommandUserHelper.NormalizeUser(sender);
        long now = nowTimestamp ?? Stopwatch.GetTimestamp();
        lock (_commandStateGate)
        {
            _viewerCommandLimitNotices.TryGetValue(viewer, out long previous);
            if (previous != 0 && now - previous < 10 * Stopwatch.Frequency)
                return false;
            _viewerCommandLimitNotices[viewer] = now;
            return true;
        }
    }

    internal bool TryReserveCustomCooldown(
        string commandName,
        string keyOwner,
        double? cooldownSeconds,
        out TimeSpan remaining,
        out CooldownReservation reservation)
    {
        remaining = TimeSpan.Zero;
        reservation = default;
        if (cooldownSeconds is not double seconds || seconds <= 0.0)
            return true;

        long nowTimestamp = Stopwatch.GetTimestamp();
        long cooldownTimestampTicks = (long)(seconds * Stopwatch.Frequency);
        (string Command, string Sender) key = (commandName, keyOwner);
        lock (_commandStateGate)
        {
            PruneCommandStateNoLock(nowTimestamp);
            _customCommandCooldownUntilTimestamp.TryGetValue(key, out long next);
            if (next != 0 && nowTimestamp < next)
            {
                remaining = Stopwatch.GetElapsedTime(nowTimestamp, next);
                return false;
            }

            long cooldownUntilTimestamp = nowTimestamp + cooldownTimestampTicks;
            _customCommandCooldownUntilTimestamp[key] = cooldownUntilTimestamp;
            reservation = new(key, cooldownUntilTimestamp, cooldownTimestampTicks);
            return true;
        }
    }

    internal void FinishCustomCooldown(CooldownReservation reservation, bool succeeded)
    {
        if (!reservation.IsActive)
            return;

        lock (_commandStateGate)
        {
            if (!_customCommandCooldownUntilTimestamp.TryGetValue(reservation.Key, out long current) ||
                current != reservation.ReservationTimestamp)
            {
                return;
            }

            if (succeeded)
                _customCommandCooldownUntilTimestamp[reservation.Key] = Stopwatch.GetTimestamp() + reservation.CooldownTimestampTicks;
            else
                _customCommandCooldownUntilTimestamp.Remove(reservation.Key);
        }
    }

    private void PruneCommandStateNoLock(long nowTimestamp)
    {
        if ((_viewerCommandTimestamps.Count <= 4096 && _customCommandCooldownUntilTimestamp.Count <= 4096) ||
            nowTimestamp - _lastCommandStatePruneTimestamp < 60 * Stopwatch.Frequency) return;
        _lastCommandStatePruneTimestamp = nowTimestamp;
        long cutoff = nowTimestamp - 60 * Stopwatch.Frequency;
        foreach ((string viewer, Queue<long> timestamps) in _viewerCommandTimestamps)
        {
            while (timestamps.Count > 0 && timestamps.Peek() <= cutoff) timestamps.Dequeue();
            if (timestamps.Count != 0) continue;
            _viewerCommandTimestamps.Remove(viewer);
            _viewerCommandLimitNotices.Remove(viewer);
        }
        foreach (var pair in _customCommandCooldownUntilTimestamp)
            if (pair.Value <= nowTimestamp) _customCommandCooldownUntilTimestamp.Remove(pair.Key);
    }

    internal void ResetCommandState()
    {
        lock (_commandStateGate)
        {
            _channelCommandTimestamps.Clear();
            _viewerCommandTimestamps.Clear();
            _viewerCommandLimitNotices.Clear();
            _customCommandCooldownUntilTimestamp.Clear();
        }
    }

    private long _lastGlobalCooldownTimestamp = -1;
    private long _lastGambleCooldownPruneTimestamp;
    private long _switchMilkTagCounter;

    public string NextSwitchMilkTag()
        => string.Create(CultureInfo.InvariantCulture, $"tc_switchmilk_{Interlocked.Increment(ref _switchMilkTagCounter)}");

    private long GlobalGameCommandCooldownTimestampTicks
    {
        get
        {
            double seconds = _config?.Settings.GlobalGameCommandCooldownSeconds ?? DefaultGlobalGameCommandCooldownSeconds;
            if (double.IsNaN(seconds) || seconds < 0.1 || seconds > 120.0)
                seconds = DefaultGlobalGameCommandCooldownSeconds;
            return (long)(seconds * Stopwatch.Frequency);
        }
    }

    public bool TryGetGlobalCooldown(out TimeSpan remaining)
    {
        if (!GlobalGameCommandCooldownEnabled)
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        long cooldownTimestampTicks = GlobalGameCommandCooldownTimestampTicks;
        long last = Interlocked.Read(ref _lastGlobalCooldownTimestamp);
        long next = last + cooldownTimestampTicks;
        long now = Stopwatch.GetTimestamp();
        if (last >= 0 && now < next)
        {
            remaining = Stopwatch.GetElapsedTime(now, next);
            return true;
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    public bool TryReserveGlobalCooldown(out TimeSpan remaining, out long reservationTimestamp)
    {
        reservationTimestamp = -1;
        if (!GlobalGameCommandCooldownEnabled)
        {
            remaining = TimeSpan.Zero;
            return true;
        }

        while (true)
        {
            long cooldownTimestampTicks = GlobalGameCommandCooldownTimestampTicks;
            long last = Interlocked.Read(ref _lastGlobalCooldownTimestamp);
            long next = last + cooldownTimestampTicks;
            long now = Stopwatch.GetTimestamp();
            if (last >= 0 && now < next)
            {
                remaining = Stopwatch.GetElapsedTime(now, next);
                return false;
            }

            if (Interlocked.CompareExchange(ref _lastGlobalCooldownTimestamp, now, last) == last)
            {
                reservationTimestamp = now;
                remaining = TimeSpan.Zero;
                return true;
            }
        }
    }

    public void ClearGlobalCooldown()
    {
        Interlocked.Exchange(ref _lastGlobalCooldownTimestamp, -1);
    }

    public void ClearGlobalCooldown(long reservationTimestamp)
    {
        if (reservationTimestamp >= 0)
            Interlocked.CompareExchange(ref _lastGlobalCooldownTimestamp, -1, reservationTimestamp);
    }

    public bool TryUseLightning(out TimeSpan remaining, out long reservationTimestamp)
    {
        if (HasGlobalCooldownOverride("lightning"))
        {
            remaining = TimeSpan.Zero;
            reservationTimestamp = 0;
            return true;
        }

        lock (_cooldownGate)
        {
            long now = Stopwatch.GetTimestamp();
            long nextAllowed = _lastLightningTimestamp + FiveMinuteCommandCooldownTimestampTicks;
            if (now < nextAllowed)
            {
                remaining = Stopwatch.GetElapsedTime(now, nextAllowed);
                reservationTimestamp = 0;
                return false;
            }

            _lastLightningTimestamp = now;
            remaining = TimeSpan.Zero;
            reservationTimestamp = now;
            return true;
        }
    }

    public void ClearLightningCooldown()
    {
        lock (_cooldownGate)
        {
            _lastLightningTimestamp = -FiveMinuteCommandCooldownTimestampTicks;
        }
    }

    public void ClearLightningCooldown(long reservationTimestamp)
    {
        lock (_cooldownGate)
        {
            if (_lastLightningTimestamp == reservationTimestamp)
                _lastLightningTimestamp = -FiveMinuteCommandCooldownTimestampTicks;
        }
    }

    internal bool TryUseScaleCommand(string commandName, out TimeSpan remaining, out long reservationTimestamp, long? nowTimestamp = null)
    {
        string normalizedCommand = (commandName ?? string.Empty).Trim();
        if (normalizedCommand.Length == 0)
            throw new ArgumentException("A command name is required.", nameof(commandName));
        if (HasGlobalCooldownOverride(normalizedCommand))
        {
            remaining = TimeSpan.Zero;
            reservationTimestamp = 0;
            return true;
        }

        lock (_cooldownGate)
        {
            long now = nowTimestamp ?? Stopwatch.GetTimestamp();
            if (_timedScaleCommandCooldowns.TryGetValue(normalizedCommand, out long lastUsed))
            {
                long nextAllowed = lastUsed + FiveMinuteCommandCooldownTimestampTicks;
                if (now < nextAllowed)
                {
                    remaining = Stopwatch.GetElapsedTime(now, nextAllowed);
                    reservationTimestamp = 0;
                    return false;
                }
            }

            _timedScaleCommandCooldowns[normalizedCommand] = now;
            remaining = TimeSpan.Zero;
            reservationTimestamp = now;
            return true;
        }
    }

    internal void ClearScaleCooldowns()
    {
        lock (_cooldownGate)
        {
            _timedScaleCommandCooldowns.Clear();
        }
    }

    internal void ClearScaleCooldown(string commandName, long reservationTimestamp)
    {
        string normalizedCommand = (commandName ?? string.Empty).Trim();
        if (normalizedCommand.Length == 0)
            return;
        lock (_cooldownGate)
        {
            if (_timedScaleCommandCooldowns.TryGetValue(normalizedCommand, out long current) && current == reservationTimestamp)
                _timedScaleCommandCooldowns.Remove(normalizedCommand);
        }
    }

    public bool IsGambleOnCooldown(string user, out TimeSpan remaining)
    {
        if (HasPerUserCooldownOverride("gambletokens"))
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        string normalized = CommandUserHelper.NormalizeUser(user);
        lock (_cooldownGate)
        {
            if (_gambleCooldowns.TryGetValue(normalized, out long until))
            {
                long now = Stopwatch.GetTimestamp();
                if (until > now)
                {
                    remaining = Stopwatch.GetElapsedTime(now, until);
                    return true;
                }

                _gambleCooldowns.Remove(normalized);
            }
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    public void StartGambleCooldown(string user, TimeSpan duration)
    {
        if (HasPerUserCooldownOverride("gambletokens"))
            return;
        string normalized = CommandUserHelper.NormalizeUser(user);
        lock (_cooldownGate)
        {
            long now = Stopwatch.GetTimestamp();
            if (_gambleCooldowns.Count > 4096 && now - _lastGambleCooldownPruneTimestamp >= 60 * Stopwatch.Frequency)
            {
                _lastGambleCooldownPruneTimestamp = now;
                foreach (KeyValuePair<string, long> pair in _gambleCooldowns)
                    if (pair.Value <= now) _gambleCooldowns.Remove(pair.Key);
            }
            _gambleCooldowns[normalized] = now + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        }
    }

    public bool IsAllowedUser(string user)
    {
        TwitchCraftConfig? config = _config;
        if (config == null)
            return false;
        string normalized = CommandUserHelper.NormalizeUser(user);
        return string.Equals(normalized, config.Twitch.StreamerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, config.Twitch.BotName, StringComparison.OrdinalIgnoreCase)
            || (config.Settings.ModeratorsCanUseStreamerCommands && _currentCommandExecution.Value?.Moderator == true);
    }

    private static string? FindOnlinePlayer(List<string> online, string playerName)
    {
        int index = SortedListHelper.FindIndex(online, playerName, StringComparer.OrdinalIgnoreCase);
        return index >= 0 ? online[index] : null;
    }

    private static ResolvedTarget MakePlayerTarget(string playerName) => new()
    {
        Selector = playerName,
        DisplayName = playerName,
        MinecraftName = playerName,
        PlayerCount = 1
    };

    public async Task<ResolvedTarget?> ResolveTargetAsync(
        IReadOnlyList<string>? args,
        int startIndex,
        string requester,
        Func<string, CancellationToken, Task> replyAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replyAsync);
        TwitchCraftConfig? config = _config;
        string defaultMinecraftPlayer = _defaultMinecraftPlayer;
        bool multiTargetingEnabled = config?.Settings.MultiplayerEnabled == true || config?.Settings.RemoteControlEnabled == true;
        if (!multiTargetingEnabled)
        {
            string streamer = config?.Twitch.StreamerName.Trim() ?? string.Empty;
            return new ResolvedTarget
            {
                Selector = "@a",
                DisplayName = streamer.Length > 0 ? streamer : (defaultMinecraftPlayer.Length == 0 ? "everyone" : defaultMinecraftPlayer),
                MinecraftName = defaultMinecraftPlayer,
                PlayerCount = 1
            };
        }

        string defaultPlayer = defaultMinecraftPlayer;
        string player = args != null && startIndex >= 0 && startIndex < args.Count
            ? (args[startIndex] ?? string.Empty).Trim()
            : string.Empty;
        List<string> online = await _refreshPlayers(cancellationToken).ConfigureAwait(false);
        if (player.Length == 0)
        {
            if (defaultPlayer.Length > 0)
            {
                string? exactDefault = FindOnlinePlayer(online, defaultPlayer);
                if (!string.IsNullOrWhiteSpace(exactDefault))
                {
                    return MakePlayerTarget(exactDefault);
                }
            }

            if (online.Count == 1)
            {
                return MakePlayerTarget(online[0]);
            }

            string message;
            if (online.Count > 1)
            {
                message = defaultPlayer.Length > 0
                    ? requester + ", " + defaultPlayer + " is not online. Please specify a player name."
                    : requester + ", please specify which player to target.";
            }
            else
            {
                message = defaultPlayer.Length > 0
                    ? requester + ", " + defaultPlayer + " is not online."
                    : requester + ", there are no players online right now.";
            }

            await replyAsync(message + " You were not charged.", cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (string.Equals(player, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (_config?.Settings.AllowAllPlayerTarget == false)
            {
                await replyAsync(requester + ", targeting every player is disabled. You were not charged.", cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (online.Count == 0)
            {
                await replyAsync(requester + ", there are no players online right now. You were not charged.", cancellationToken).ConfigureAwait(false);
                return null;
            }

            return new ResolvedTarget
            {
                Selector = "@a",
                DisplayName = "everyone",
                MinecraftName = string.Empty,
                PlayerCount = online.Count
            };
        }

        if (!MinecraftNameHelper.IsValidPlayerName(player))
        {
            if (defaultPlayer.Length > 0 && int.TryParse(player, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                string? exactDefault = FindOnlinePlayer(online, defaultPlayer);
                if (!string.IsNullOrWhiteSpace(exactDefault))
                {
                    return MakePlayerTarget(exactDefault);
                }
            }

            await replyAsync(requester + ", invalid player name '" + player + "'. You were not charged.", cancellationToken).ConfigureAwait(false);
            return null;
        }

        string? exactPlayer = FindOnlinePlayer(online, player);
        if (string.IsNullOrWhiteSpace(exactPlayer))
        {
            await replyAsync(requester + ", player '" + player + "' is not online. You were not charged.", cancellationToken).ConfigureAwait(false);
            return null;
        }

        return MakePlayerTarget(exactPlayer);
    }
}
