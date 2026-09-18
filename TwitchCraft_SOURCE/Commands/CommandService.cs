using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

/// Owns command targeting, authorization, costs, and command-specific cooldown state.
public sealed class CommandService
{
    private const double DefaultGlobalGameCommandCooldownSeconds = 10.0;
    private static readonly TimeSpan FiveMinuteCommandCooldown = TimeSpan.FromMinutes(5);
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
    private readonly Dictionary<(string Command, string Sender), long> _customCommandCooldownUntilTicks = [];
    private readonly Dictionary<string, DateTime> _timedScaleCommandCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _gambleCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private long _lastChannelCommandLimitNoticeTicks;
    private long _lastCommandStatePruneTicks;
    private DateTime _lastLightningUtc;
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

    internal readonly record struct CooldownReservation((string Command, string Sender) Key, long ReservationTicks, long CooldownTicks)
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

    internal bool TryUseCommandSlots(string viewer, out bool viewerLimited, long? nowTicks = null)
    {
        int viewerLimit = _config?.Settings.ViewerCommandLimitPerMinute ?? 0;
        int channelLimit = _config?.Settings.ChannelCommandLimitPerMinute ?? 0;
        viewerLimited = false;
        if (viewerLimit <= 0 && channelLimit <= 0)
            return true;

        long now = nowTicks ?? DateTime.UtcNow.Ticks, cutoff = now - TimeSpan.TicksPerMinute;
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

    internal bool ShouldWarnChannelLimit(long? nowTicks = null)
    {
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        long previous = Volatile.Read(ref _lastChannelCommandLimitNoticeTicks);
        if (previous != 0 && now - previous < 10 * TimeSpan.TicksPerSecond)
            return false;
        return Interlocked.CompareExchange(ref _lastChannelCommandLimitNoticeTicks, now, previous) == previous;
    }

    internal bool ShouldWarnViewerLimit(string sender, long? nowTicks = null)
    {
        string viewer = CommandUserHelper.NormalizeUser(sender);
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_commandStateGate)
        {
            _viewerCommandLimitNotices.TryGetValue(viewer, out long previous);
            if (previous != 0 && now - previous < 10 * TimeSpan.TicksPerSecond)
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

        long nowTicks = DateTime.UtcNow.Ticks;
        long cooldownTicks = (long)(seconds * TimeSpan.TicksPerSecond);
        (string Command, string Sender) key = (commandName, keyOwner);
        lock (_commandStateGate)
        {
            PruneCommandStateNoLock(nowTicks);
            _customCommandCooldownUntilTicks.TryGetValue(key, out long next);
            if (next != 0 && nowTicks < next)
            {
                remaining = TimeSpan.FromTicks(next - nowTicks);
                return false;
            }

            long cooldownUntilTicks = nowTicks + cooldownTicks;
            _customCommandCooldownUntilTicks[key] = cooldownUntilTicks;
            reservation = new(key, cooldownUntilTicks, cooldownTicks);
            return true;
        }
    }

    internal void FinishCustomCooldown(CooldownReservation reservation, bool succeeded)
    {
        if (!reservation.IsActive)
            return;

        lock (_commandStateGate)
        {
            if (!_customCommandCooldownUntilTicks.TryGetValue(reservation.Key, out long current) ||
                current != reservation.ReservationTicks)
            {
                return;
            }

            if (succeeded)
                _customCommandCooldownUntilTicks[reservation.Key] = DateTime.UtcNow.Ticks + reservation.CooldownTicks;
            else
                _customCommandCooldownUntilTicks.Remove(reservation.Key);
        }
    }

    private void PruneCommandStateNoLock(long nowTicks)
    {
        if ((_viewerCommandTimestamps.Count <= 4096 && _customCommandCooldownUntilTicks.Count <= 4096) ||
            nowTicks - _lastCommandStatePruneTicks < TimeSpan.TicksPerMinute) return;
        _lastCommandStatePruneTicks = nowTicks;
        long cutoff = nowTicks - TimeSpan.TicksPerMinute;
        foreach ((string viewer, Queue<long> timestamps) in _viewerCommandTimestamps)
        {
            while (timestamps.Count > 0 && timestamps.Peek() <= cutoff) timestamps.Dequeue();
            if (timestamps.Count != 0) continue;
            _viewerCommandTimestamps.Remove(viewer);
            _viewerCommandLimitNotices.Remove(viewer);
        }
        foreach (var pair in _customCommandCooldownUntilTicks)
            if (pair.Value <= nowTicks) _customCommandCooldownUntilTicks.Remove(pair.Key);
    }

    internal void ResetCommandState()
    {
        lock (_commandStateGate)
        {
            _channelCommandTimestamps.Clear();
            _viewerCommandTimestamps.Clear();
            _viewerCommandLimitNotices.Clear();
            _customCommandCooldownUntilTicks.Clear();
        }
    }

    private long _lastTicks;
    private long _lastGambleCooldownPruneTicks;
    private long _switchMilkTagCounter;

    public string NextSwitchMilkTag()
        => string.Create(CultureInfo.InvariantCulture, $"tc_switchmilk_{Interlocked.Increment(ref _switchMilkTagCounter)}");

    private long GlobalGameCommandCooldownTicks
    {
        get
        {
            double seconds = _config?.Settings.GlobalGameCommandCooldownSeconds ?? DefaultGlobalGameCommandCooldownSeconds;
            if (double.IsNaN(seconds) || seconds < 0.1 || seconds > 120.0)
                seconds = DefaultGlobalGameCommandCooldownSeconds;
            return TimeSpan.FromSeconds(seconds).Ticks;
        }
    }

    public bool TryGetGlobalCooldown(out TimeSpan remaining)
    {
        if (!GlobalGameCommandCooldownEnabled)
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        long cooldownTicks = GlobalGameCommandCooldownTicks;
        long last = Interlocked.Read(ref _lastTicks);
        long next = last + cooldownTicks;
        long now = DateTime.UtcNow.Ticks;
        if (now < next)
        {
            remaining = TimeSpan.FromTicks(next - now);
            return true;
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    public bool TryReserveGlobalCooldown(out TimeSpan remaining, out long reservationTicks)
    {
        reservationTicks = 0;
        if (!GlobalGameCommandCooldownEnabled)
        {
            remaining = TimeSpan.Zero;
            return true;
        }

        while (true)
        {
            long cooldownTicks = GlobalGameCommandCooldownTicks;
            long last = Interlocked.Read(ref _lastTicks);
            long next = last + cooldownTicks;
            long now = DateTime.UtcNow.Ticks;
            if (now < next)
            {
                remaining = TimeSpan.FromTicks(next - now);
                return false;
            }

            if (Interlocked.CompareExchange(ref _lastTicks, now, last) == last)
            {
                reservationTicks = now;
                remaining = TimeSpan.Zero;
                return true;
            }
        }
    }

    public void ClearGlobalCooldown()
    {
        Interlocked.Exchange(ref _lastTicks, 0);
    }

    public void ClearGlobalCooldown(long reservationTicks)
    {
        if (reservationTicks > 0)
            Interlocked.CompareExchange(ref _lastTicks, 0, reservationTicks);
    }

    public bool TryUseLightning(out TimeSpan remaining, out DateTime reservationUtc)
    {
        if (HasGlobalCooldownOverride("lightning"))
        {
            remaining = TimeSpan.Zero;
            reservationUtc = DateTime.MinValue;
            return true;
        }

        lock (_cooldownGate)
        {
            DateTime now = DateTime.UtcNow;
            DateTime nextAllowed = _lastLightningUtc + FiveMinuteCommandCooldown;
            if (now < nextAllowed)
            {
                remaining = nextAllowed - now;
                reservationUtc = DateTime.MinValue;
                return false;
            }

            _lastLightningUtc = now;
            remaining = TimeSpan.Zero;
            reservationUtc = now;
            return true;
        }
    }

    public void ClearLightningCooldown()
    {
        lock (_cooldownGate)
        {
            _lastLightningUtc = DateTime.MinValue;
        }
    }

    public void ClearLightningCooldown(DateTime reservationUtc)
    {
        lock (_cooldownGate)
        {
            if (_lastLightningUtc == reservationUtc)
                _lastLightningUtc = DateTime.MinValue;
        }
    }

    internal bool TryUseScaleCommand(string commandName, out TimeSpan remaining, out DateTime reservationUtc, DateTime? nowUtc = null)
    {
        string normalizedCommand = (commandName ?? string.Empty).Trim();
        if (normalizedCommand.Length == 0)
            throw new ArgumentException("A command name is required.", nameof(commandName));
        if (HasGlobalCooldownOverride(normalizedCommand))
        {
            remaining = TimeSpan.Zero;
            reservationUtc = DateTime.MinValue;
            return true;
        }

        lock (_cooldownGate)
        {
            DateTime now = nowUtc ?? DateTime.UtcNow;
            if (_timedScaleCommandCooldowns.TryGetValue(normalizedCommand, out DateTime lastUsedUtc))
            {
                DateTime nextAllowed = lastUsedUtc + FiveMinuteCommandCooldown;
                if (now < nextAllowed)
                {
                    remaining = nextAllowed - now;
                    reservationUtc = DateTime.MinValue;
                    return false;
                }
            }

            _timedScaleCommandCooldowns[normalizedCommand] = now;
            remaining = TimeSpan.Zero;
            reservationUtc = now;
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

    internal void ClearScaleCooldown(string commandName, DateTime reservationUtc)
    {
        string normalizedCommand = (commandName ?? string.Empty).Trim();
        if (normalizedCommand.Length == 0 || reservationUtc == DateTime.MinValue)
            return;
        lock (_cooldownGate)
        {
            if (_timedScaleCommandCooldowns.TryGetValue(normalizedCommand, out DateTime current) && current == reservationUtc)
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
            if (_gambleCooldowns.TryGetValue(normalized, out DateTime until))
            {
                DateTime now = DateTime.UtcNow;
                if (until > now)
                {
                    remaining = until - now;
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
            DateTime now = DateTime.UtcNow;
            if (_gambleCooldowns.Count > 4096 && now.Ticks - _lastGambleCooldownPruneTicks >= TimeSpan.TicksPerMinute)
            {
                _lastGambleCooldownPruneTicks = now.Ticks;
                foreach (KeyValuePair<string, DateTime> pair in _gambleCooldowns)
                    if (pair.Value <= now) _gambleCooldowns.Remove(pair.Key);
            }
            _gambleCooldowns[normalized] = now + duration;
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
