using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly StartingProfile DefaultEffectiveSettings = new();
    private readonly Dictionary<string, long> _viewerLastChatActivity = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowConnectionHealth => EffectiveSettings.ShowConnectionHealth;
    public bool TwitchChatConnected => _twitchSession.ChatConnected;
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

    internal string CommandPrefix => EffectiveSettings.CommandPrefix;
    internal string SecondaryCommandPrefix => EffectiveSettings.SecondaryCommandPrefix;
    internal string BotResponseVerbosity => EffectiveSettings.BotResponseVerbosity;

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
        if (!EffectiveSettings.ShowExactCooldownRemaining)
            return "a moment";

        int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        int minutes = seconds / 60;
        int leftover = seconds % 60;
        return minutes > 0
            ? minutes.ToString(CultureInfo.InvariantCulture) + "m " + leftover.ToString(CultureInfo.InvariantCulture) + "s"
            : seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }

    internal int GetPassivePayoutDelay()
    {
        StartingProfile settings = EffectiveSettings;
        int minimum = settings.PassiveTokenPayoutMinimumSeconds;
        int maximum = settings.PassiveTokenPayoutMaximumSeconds;
        return minimum == maximum ? minimum : Random.Shared.Next(minimum, maximum + 1);
    }

    internal int PassiveTokensPerPayout => EffectiveSettings.PassiveTokensPerPayout;

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

    private bool AreViewerCommandsPaused(string sender)
        => EffectiveSettings.ViewerCommandsPaused &&
            !string.Equals(sender, _currentStreamerName, StringComparison.OrdinalIgnoreCase);
}
