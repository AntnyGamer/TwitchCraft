using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly StartingProfile DefaultEffectiveSettings = new();
    private readonly Queue<long> _relayMessageTimestamps = new();
    private readonly Dictionary<string, long> _viewerLastChatActivity = new(StringComparer.OrdinalIgnoreCase);
    private int _twitchChatConnected;

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

    internal string CommandPrefix => _activeConfig?.Settings.CommandPrefix ?? "!";
    internal string SecondaryCommandPrefix => _activeConfig?.Settings.SecondaryCommandPrefix ?? string.Empty;
    internal string MinecraftRelayTextColor => _activeConfig?.Settings.MinecraftRelayTextColor ?? "white";
    internal string BotResponseVerbosity => _activeConfig?.Settings.BotResponseVerbosity ?? BotResponseVerbositySettings.Normal;

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
        for (int i = message.IndexOf('!'); i >= 0 && i + 1 < message.Length; i = message.IndexOf('!', i + 1))
        {
            if (!char.IsAsciiLetter(message[i + 1]) ||
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

    internal int GetPassivePayoutDelay()
    {
        StartingProfile settings = EffectiveSettings;
        int minimum = settings.PassiveTokenPayoutMinimumSeconds;
        int maximum = settings.PassiveTokenPayoutMaximumSeconds;
        return minimum == maximum ? minimum : Random.Shared.Next(minimum, maximum + 1);
    }

    internal int PassiveTokensPerPayout => _activeConfig?.Settings.PassiveTokensPerPayout ?? 1;

    internal void RecordChatActivity(string sender, long unixSeconds)
    {
        if (!EffectiveSettings.PassiveRewardsRequireActivity)
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
        if (!settings.PassiveRewardsRequireActivity)
            return true;

        return _viewerLastChatActivity.TryGetValue(viewer, out long lastActive) &&
            nowUnixSeconds - lastActive <= settings.PassiveActivityWindowMinutes * 60L;
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
        lock (_relayGate)
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
