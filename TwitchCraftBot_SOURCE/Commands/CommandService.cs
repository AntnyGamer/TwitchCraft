using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

/// <summary>
/// Owns command targeting, authorization, costs, and command-specific cooldown state.
/// </summary>
public sealed class CommandService
{
    private const double DefaultGlobalGameCommandCooldownSeconds = 10.0;
    private static readonly TimeSpan FiveMinuteCommandCooldown = TimeSpan.FromMinutes(5);
    private BotConfig? _config;
    private string _defaultMinecraftPlayer = string.Empty;
    private readonly Func<string?, bool> _hasGlobalCooldownOverride;
    private readonly Func<string, bool> _hasPerUserCooldownOverride;
    private readonly Func<CancellationToken, Task<List<string>>> _refreshPlayers;
    private readonly Lock _cooldownGate = new();
    private readonly AsyncLocal<bool> _currentCommandSenderIsModerator = new();
    private readonly Dictionary<string, DateTime> _timedScaleCommandCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _gambleCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastLightningUtc;
    private int _fireworksRepeatActive;

    internal CommandService(
        Func<string?, bool> hasGlobalCooldownOverride,
        Func<string, bool> hasPerUserCooldownOverride,
        Func<CancellationToken, Task<List<string>>> refreshPlayers)
    {
        ArgumentNullException.ThrowIfNull(hasGlobalCooldownOverride);
        ArgumentNullException.ThrowIfNull(hasPerUserCooldownOverride);
        ArgumentNullException.ThrowIfNull(refreshPlayers);
        _hasGlobalCooldownOverride = hasGlobalCooldownOverride;
        _hasPerUserCooldownOverride = hasPerUserCooldownOverride;
        _refreshPlayers = refreshPlayers;
    }

    internal void SetContext(BotConfig? config, string defaultMinecraftPlayer)
    {
        _config = config;
        _defaultMinecraftPlayer = defaultMinecraftPlayer;
    }

    public int ScaleCost(int baseCost, int playerCount)
    {
        if (baseCost <= 0)
        {
            return 0;
        }

        BotConfig? config = _config;
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

    public bool GlobalGameCommandCooldownEnabled
        => _config?.Settings.GlobalGameCommandCooldownEnabled == true && !_hasGlobalCooldownOverride(null);

    public void SetModerator(bool isModerator)
        => _currentCommandSenderIsModerator.Value = isModerator;

    private long _lastTicks;
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
        if (_hasGlobalCooldownOverride("lightning"))
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

    internal bool TryUseScaleCommand(string commandName, out TimeSpan remaining, out DateTime reservationUtc)
    {
        string normalizedCommand = (commandName ?? string.Empty).Trim();
        if (normalizedCommand.Length == 0)
            throw new ArgumentException("A command name is required.", nameof(commandName));
        if (_hasGlobalCooldownOverride(normalizedCommand))
        {
            remaining = TimeSpan.Zero;
            reservationUtc = DateTime.MinValue;
            return true;
        }

        lock (_cooldownGate)
        {
            DateTime now = DateTime.UtcNow;
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
        if (_hasPerUserCooldownOverride("gambletokens"))
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
        if (_hasPerUserCooldownOverride("gambletokens"))
            return;
        string normalized = CommandUserHelper.NormalizeUser(user);
        lock (_cooldownGate)
        {
            _gambleCooldowns[normalized] = DateTime.UtcNow + duration;
        }

    }

    public bool IsAllowedUser(string user)
    {
        BotConfig? config = _config;
        if (config == null)
            return false;
        string normalized = CommandUserHelper.NormalizeUser(user);
        return string.Equals(normalized, config.Twitch.StreamerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, config.Twitch.BotName, StringComparison.OrdinalIgnoreCase)
            || (config.Settings.ModeratorsCanUseStreamerCommands && _currentCommandSenderIsModerator.Value);
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
        BotConfig? config = _config;
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
