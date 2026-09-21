using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly StartingProfile DefaultSettings = new();
    private readonly Dictionary<string, long> _viewerLastChatActivity = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowConnectionHealth => CurrentSettings.ShowConnectionHealth;
    public bool TwitchChatConnected => _twitchSession.ChatConnected;
    public bool LowResourceModeEnabled => CurrentSettings.LowResourceModeEnabled;
    public bool PauseUIUpdatesWhenMinimized => CurrentSettings.PauseUIUpdatesWhenMinimized || CurrentSettings.LowResourceModeEnabled;
    public int MaxVisibleTwitchLogLines => CurrentSettings.LowResourceModeEnabled ? Math.Min(100, CurrentSettings.MaxVisibleTwitchLogLines) : CurrentSettings.MaxVisibleTwitchLogLines;
    public int MaxVisibleMinecraftLogLines => CurrentSettings.LowResourceModeEnabled ? Math.Min(100, CurrentSettings.MaxVisibleMinecraftLogLines) : CurrentSettings.MaxVisibleMinecraftLogLines;
    internal int ViewerRosterRefreshIntervalSeconds => CurrentSettings.LowResourceModeEnabled ? Math.Max(60, CurrentSettings.ViewerRosterRefreshIntervalSeconds) : CurrentSettings.ViewerRosterRefreshIntervalSeconds;
    internal int MaxGameplayCommandQueue => CurrentSettings.LowResourceModeEnabled ? Math.Min(35, CurrentSettings.MaxGameplayCommandQueue) : CurrentSettings.MaxGameplayCommandQueue;
    internal TimeSpan RCONTimeout => TimeSpan.FromSeconds(CurrentSettings.RCONTimeoutSeconds);
    internal TimeSpan GracefulShutdownTimeout => TimeSpan.FromSeconds(CurrentSettings.GracefulShutdownTimeoutSeconds);
    internal IReadOnlyList<string> RegisteredCommandNames => _commandRegistry.CommandNames;

    private StartingProfile CurrentSettings => _activeConfig?.Settings ?? DefaultSettings;

    internal string CommandPrefix => CurrentSettings.CommandPrefix;
    internal string SecondaryCommandPrefix => CurrentSettings.SecondaryCommandPrefix;
    internal string BotResponseVerbosity => CurrentSettings.BotResponseVerbosity;

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
        if (!CurrentSettings.ShowExactCooldownRemaining)
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
        StartingProfile settings = CurrentSettings;
        int minimum = settings.PassiveTokenPayoutMinimumSeconds;
        int maximum = settings.PassiveTokenPayoutMaximumSeconds;
        return minimum == maximum ? minimum : Random.Shared.Next(minimum, maximum + 1);
    }

    internal int PassiveTokensPerPayout => CurrentSettings.PassiveTokensPerPayout;

    internal void RecordChatActivity(string sender, long unixSeconds)
    {
        if (!CurrentSettings.PassiveRewardsRequireActivity)
            return;

        string normalizedSender = NormalizeUser(sender);
        if (normalizedSender.Length == 0)
            return;

        lock (_viewerGate)
            _viewerLastChatActivity[normalizedSender] = unixSeconds;
    }

    internal bool IsRewardEligibleNoLock(string viewer, long nowUnixSeconds)
    {
        StartingProfile settings = CurrentSettings;
        if (!settings.PassiveRewardsRequireActivity)
            return true;

        return _viewerLastChatActivity.TryGetValue(viewer, out long lastActive) &&
            nowUnixSeconds - lastActive <= settings.PassiveActivityWindowMinutes * 60L;
    }

    private bool AreViewerCommandsPaused(string sender)
        => CurrentSettings.ViewerCommandsPaused &&
            !string.Equals(sender, _currentStreamerName, StringComparison.OrdinalIgnoreCase);
}
