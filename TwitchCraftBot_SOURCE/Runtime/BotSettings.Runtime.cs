using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private readonly record struct CustomCommandCooldownReservation((string Command, string Sender) Key, long ReservationTicks)
    {
        internal bool IsActive => Key.Command != null;
    }

    private sealed class CommandExecutionState
    {
        internal bool Succeeded;
    }

    private static readonly StartingProfile DefaultEffectiveSettings = new();
    private readonly AsyncLocal<string?> _currentCommandSender = new();
    private readonly AsyncLocal<CommandExecutionState?> _currentCommandExecution = new();
    private readonly Queue<long> _channelCommandTimestamps = new();
    private readonly Dictionary<string, Queue<long>> _viewerCommandTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _viewerCommandLimitNotices = new(StringComparer.OrdinalIgnoreCase);
    private const string GlobalCooldownKey = "\0";
    private readonly Dictionary<(string Command, string Sender), long> _customCommandLastUsedTicks = [];
    private readonly Queue<long> _relayMessageTimestamps = new();
    private readonly Dictionary<string, long> _viewerLastChatActivity = new(StringComparer.OrdinalIgnoreCase);
    private long _lastChannelCommandLimitNoticeTicks;
    private int _twitchChatConnected;

    public int MaximumTokenBalance => Math.Max(0, _activeConfig?.Settings.MaximumTokenBalance ?? 0);
    public bool AllowAllPlayerTarget => _activeConfig?.Settings.AllowAllPlayerTarget ?? true;
    public bool AllowRandomPlayerTarget => _activeConfig?.Settings.AllowRandomPlayerTarget ?? true;
    public bool ShowConnectionHealth => _activeConfig?.Settings.ShowConnectionHealth ?? false;
    public bool TwitchChatConnected => Volatile.Read(ref _twitchChatConnected) != 0;
    public bool LowResourceModeEnabled => EffectiveSettings.LowResourceModeEnabled;
    public bool PauseUIUpdatesWhenMinimized => EffectiveSettings.PauseUIUpdatesWhenMinimized || EffectiveSettings.LowResourceModeEnabled;
    public int MaxVisibleTwitchLogLines => EffectiveSettings.LowResourceModeEnabled ? Math.Min(100, EffectiveSettings.MaxVisibleTwitchLogLines) : EffectiveSettings.MaxVisibleTwitchLogLines;
    public int MaxVisibleMinecraftLogLines => EffectiveSettings.LowResourceModeEnabled ? Math.Min(100, EffectiveSettings.MaxVisibleMinecraftLogLines) : EffectiveSettings.MaxVisibleMinecraftLogLines;
    internal int ViewerRosterRefreshIntervalSeconds => EffectiveSettings.LowResourceModeEnabled ? Math.Max(60, EffectiveSettings.ViewerRosterRefreshIntervalSeconds) : EffectiveSettings.ViewerRosterRefreshIntervalSeconds;
    internal int MaxGameplayCommandQueue => EffectiveSettings.LowResourceModeEnabled ? Math.Min(35, EffectiveSettings.MaxGameplayCommandQueue) : EffectiveSettings.MaxGameplayCommandQueue;
    internal TimeSpan RCONTimeout => TimeSpan.FromSeconds(EffectiveSettings.RCONTimeoutSeconds);
    internal TimeSpan GracefulShutdownTimeout => TimeSpan.FromSeconds(EffectiveSettings.GracefulShutdownTimeoutSeconds);
    internal IReadOnlyList<string> RegisteredCommandNames => _commandRegistry.CommandNames;

    private StartingProfile EffectiveSettings => _activeConfig?.Settings ?? DefaultEffectiveSettings;

    internal string CommandPrefix => _currentCommandPrefix;
    internal string SecondaryCommandPrefix => _currentSecondaryCommandPrefix;
    internal string MinecraftRelayTextColor => _currentMinecraftRelayTextColor;
    internal string BotResponseVerbosity => _currentBotResponseVerbosity;

    internal static bool TryMatchPrefix(
        string payload,
        string primaryPrefix,
        string secondaryPrefix,
        out string matchedPrefix)
    {
        matchedPrefix = string.Empty;
        if (string.IsNullOrEmpty(payload))
            return false;

        if (secondaryPrefix.Length > primaryPrefix.Length && payload.StartsWith(secondaryPrefix, StringComparison.Ordinal))
        {
            matchedPrefix = secondaryPrefix;
            return true;
        }
        if (payload.StartsWith(primaryPrefix, StringComparison.Ordinal))
        {
            matchedPrefix = primaryPrefix;
            return true;
        }
        if (secondaryPrefix.Length > 0 && payload.StartsWith(secondaryPrefix, StringComparison.Ordinal))
        {
            matchedPrefix = secondaryPrefix;
            return true;
        }

        return false;
    }

    internal static string FormatReply(string message, string sender, bool mentionViewer)
    {
        if (!mentionViewer || sender.Length == 0 || string.IsNullOrWhiteSpace(message))
            return message;

        if (message.Length > sender.Length &&
            message[0] == '@' &&
            message.AsSpan(1).StartsWith(sender.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return message;
        }

        if (message.Length > sender.Length &&
            message[sender.Length] == ',' &&
            message.AsSpan(0, sender.Length).Equals(sender.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return "@" + message;
        }

        return "@" + sender + " " + message;
    }

    internal static string ApplyPrefix(string message, string prefix)
    {
        if (prefix == "!" || string.IsNullOrEmpty(message))
            return message;

        StringBuilder? builder = null;
        int copyStart = 0;
        for (int i = 0; i + 1 < message.Length; i++)
        {
            if (message[i] != '!' || !char.IsAsciiLetter(message[i + 1]) ||
                i > 0 && (char.IsAsciiLetterOrDigit(message[i - 1]) || message[i - 1] == '_'))
            {
                continue;
            }

            builder ??= new StringBuilder(message.Length + 8);
            builder.Append(message, copyStart, i - copyStart).Append(prefix);
            copyStart = i + 1;
        }

        return builder == null ? message : builder.Append(message, copyStart, message.Length - copyStart).ToString();
    }

    internal string FormatCooldown(TimeSpan remaining)
    {
        if (_activeConfig?.Settings.ShowExactCooldownRemaining == false)
            return "a moment";

        int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        int minutes = seconds / 60;
        int leftover = seconds % 60;
        return minutes > 0
            ? minutes.ToString(CultureInfo.InvariantCulture) + "m " + leftover.ToString(CultureInfo.InvariantCulture) + "s"
            : seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }

    internal static string FormatRelay(string sender, string payload, bool includeTimestamp, DateTime localTime)
    {
        string prefix = includeTimestamp
            ? "[" + localTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "] "
            : string.Empty;
        return prefix + sender + ": " + payload;
    }

    internal int GetPayoutDelay()
    {
        StartingProfile settings = EffectiveSettings;
        int minimum = Math.Clamp(settings.PassiveTokenPayoutMinimumSeconds, 10, 900);
        int maximum = Math.Clamp(settings.PassiveTokenPayoutMaximumSeconds, 10, 900);
        if (minimum > maximum)
            (minimum, maximum) = (maximum, minimum);
        return minimum == maximum ? minimum : Random.Shared.Next(minimum, maximum + 1);
    }

    internal int PassiveTokensPerPayout
    {
        get
        {
            int amount = _activeConfig?.Settings.PassiveTokensPerPayout ?? 1;
            return amount is >= 1 and <= 1_000_000 ? amount : 1;
        }
    }

    internal void RecordChatActivity(string sender, long unixSeconds)
    {
        if (!EffectiveSettings.PassiveRewardsRequireRecentChat)
            return;

        string normalizedSender = NormalizeUser(sender);
        if (normalizedSender.Length == 0)
            return;

        lock (_viewerGate)
            _viewerLastChatActivity[normalizedSender] = unixSeconds;
    }

    internal bool IsRewardEligibleNoLock(string viewer, long nowUnixSeconds)
    {
        StartingProfile settings = EffectiveSettings;
        if (!settings.PassiveRewardsRequireRecentChat)
            return true;

        int minutes = Math.Clamp(settings.PassiveRecentChatWindowMinutes, 1, 120);
        return _viewerLastChatActivity.TryGetValue(viewer, out long lastActive) &&
            nowUnixSeconds - lastActive <= minutes * 60L;
    }

    internal bool TryUseCommandSlots(string viewer, out bool viewerLimited, long? nowTicks = null)
    {
        int viewerLimit = _activeConfig?.Settings.ViewerCommandLimitPerMinute ?? 0;
        int channelLimit = _activeConfig?.Settings.ChannelCommandLimitPerMinute ?? 0;
        viewerLimited = false;
        if (viewerLimit <= 0 && channelLimit <= 0)
            return true;
        long now = nowTicks ?? DateTime.UtcNow.Ticks, cutoff = now - TimeSpan.TicksPerMinute;
        lock (_cooldownGate)
        {
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
        string viewer = NormalizeUser(sender);
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_cooldownGate)
        {
            _viewerCommandLimitNotices.TryGetValue(viewer, out long previous);
            if (previous != 0 && now - previous < 10 * TimeSpan.TicksPerSecond)
                return false;
            _viewerCommandLimitNotices[viewer] = now;
            return true;
        }
    }

    private bool TryReserveCustomCooldown(
        string commandName,
        string keyOwner,
        double? cooldownSeconds,
        out TimeSpan remaining,
        out CustomCommandCooldownReservation reservation)
    {
        remaining = TimeSpan.Zero;
        reservation = default;
        if (cooldownSeconds is not double seconds || seconds <= 0.0)
            return true;

        long nowTicks = DateTime.UtcNow.Ticks;
        long cooldownTicks = (long)(seconds * TimeSpan.TicksPerSecond);
        (string Command, string Sender) key = (commandName, keyOwner);
        lock (_cooldownGate)
        {
            _customCommandLastUsedTicks.TryGetValue(key, out long previous);
            long next = previous + cooldownTicks;
            if (previous != 0 && nowTicks < next)
            {
                remaining = TimeSpan.FromTicks(next - nowTicks);
                return false;
            }

            _customCommandLastUsedTicks[key] = nowTicks;
            reservation = new(key, nowTicks);
            return true;
        }
    }

    private void FinishCustomCooldown(
        CustomCommandCooldownReservation reservation,
        bool succeeded)
    {
        if (!reservation.IsActive)
            return;

        lock (_cooldownGate)
        {
            if (!_customCommandLastUsedTicks.TryGetValue(reservation.Key, out long current) ||
                current != reservation.ReservationTicks)
            {
                return;
            }

            if (succeeded)
                _customCommandLastUsedTicks[reservation.Key] = DateTime.UtcNow.Ticks;
            else
                _customCommandLastUsedTicks.Remove(reservation.Key);
        }
    }

    private void BeginCommand() => _currentCommandExecution.Value = new();

    internal void MarkCommandSuccess()
    {
        if (_currentCommandExecution.Value is CommandExecutionState state)
            state.Succeeded = true;
    }

    private bool CommandSucceeded => _currentCommandExecution.Value?.Succeeded == true;

    private void EndCommand() => _currentCommandExecution.Value = null;

    internal bool HasPerUserCooldownOverride(string? commandName = null)
    {
        string name = (commandName ?? Statistics.CurrentCommandName).Trim();
        return TryGetCommandSettings(name, out CommandCustomization customization) && customization.CooldownSeconds.HasValue;
    }

    internal bool HasGlobalCooldownOverride(string? commandName = null)
    {
        string name = (commandName ?? Statistics.CurrentCommandName).Trim();
        return TryGetCommandSettings(name, out CommandCustomization customization) && customization.GlobalCooldownSeconds.HasValue;
    }

    private bool TryGetCommandSettings(string? commandName, out CommandCustomization customization)
    {
        customization = null!;
        Dictionary<string, CommandCustomization>? customizations = _activeConfig?.Settings.CommandCustomizations;
        if (customizations == null || customizations.Count == 0)
            return false;

        string name = (commandName ?? string.Empty).Trim();
        if (name.Length == 0 || !customizations.TryGetValue(name, out CommandCustomization? found) || found == null)
            return false;

        customization = found;
        return true;
    }

    internal bool TryUseRelaySlot(long? nowTicks = null)
    {
        StartingProfile settings = EffectiveSettings;
        int limit = settings.MinecraftRelayMessagesPerSecond;
        if (settings.LowResourceModeEnabled)
            limit = limit <= 0 ? 5 : Math.Min(limit, 5);
        if (limit <= 0)
            return true;

        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_cooldownGate)
        {
            long cutoff = now - TimeSpan.TicksPerSecond;
            while (_relayMessageTimestamps.Count > 0 && _relayMessageTimestamps.Peek() <= cutoff)
                _relayMessageTimestamps.Dequeue();
            if (_relayMessageTimestamps.Count >= limit)
                return false;
            _relayMessageTimestamps.Enqueue(now);
            return true;
        }
    }

    private bool AreViewerCommandsPaused(string sender)
        => _activeConfig?.Settings.ViewerCommandsPaused == true &&
            !string.Equals(sender, _currentStreamerName, StringComparison.OrdinalIgnoreCase);

    private void SetChatConnected(bool connected)
        => Volatile.Write(ref _twitchChatConnected, connected ? 1 : 0);
}
