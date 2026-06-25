using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TwitchCraftBot_V1.Frames;

public partial class Statistics : UserControl
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TwitchCraftBot? _parentBot;
    private CancellationTokenSource? _refreshCancellation;
    private bool _refreshing;

    public Statistics()
    {
        InitializeComponent();
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += (_, _) => StartRefreshTimerIfVisible();
        Unloaded += (_, _) => StopRefreshTimer(clearParent: true);
        IsVisibleChanged += (_, _) => { if (IsVisible) StartRefreshTimerIfVisible(); else StopRefreshTimer(clearParent: false); };
    }

    private void StartRefreshTimerIfVisible()
    {
        if (!IsVisible || _refreshTimer.IsEnabled || _refreshing)
            return;

        _refreshTimer.Start();
        RefreshTimer_Tick(this, EventArgs.Empty);
    }

    private void StopRefreshTimer(bool clearParent)
    {
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
        if (clearParent)
            _parentBot = null;
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_refreshing)
            return;

        _refreshTimer.Stop();
        _refreshing = true;
        CancellationTokenSource refreshCancellation = new();
        _refreshCancellation = refreshCancellation;
        CancellationToken refreshToken = refreshCancellation.Token;

        try
        {
            _parentBot ??= AppHelpers.GetParentBot(this);
            if (_parentBot is not TwitchCraftBot parent || !IsVisible)
                return;

            BotStatisticsSnapshot? stats = await Task.Run(() => parent.GetStatisticsSnapshot(refreshToken), refreshToken);
            if (stats != null && IsVisible && !refreshToken.IsCancellationRequested)
                ApplyStatistics(stats);
        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Statistics refresh failed", ex);
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, refreshCancellation))
                _refreshCancellation = null;
            refreshCancellation.Dispose();
            _refreshing = false;
            if (IsVisible)
                _refreshTimer.Start();
        }
    }

    private void ApplyStatistics(BotStatisticsSnapshot stats)
    {
        Visibility disabledVisibility = stats.StatisticsEnabled ? Visibility.Collapsed : Visibility.Visible;
        if (StatisticsDisabledBox.Visibility != disabledVisibility)
            StatisticsDisabledBox.Visibility = disabledVisibility;

        SetTextIfChanged(SessionCommandsText, FormatNumber(stats.SessionGameCommandsRun));
        SetTextIfChanged(SessionMostUsedCommandText, FormatName(stats.SessionMostUsedCommand));
        SetTextIfChanged(SessionTokensSpentText, FormatNumber(stats.SessionTokensSpent));
        SetTextIfChanged(SessionEffectsText, FormatNumber(stats.SessionEffectsGiven));
        SetTextIfChanged(SessionDangerousText, FormatName(stats.SessionMostDangerousViewer));
        SetTextIfChanged(SessionNicestText, FormatName(stats.SessionNicestViewer));
        SetTextIfChanged(SessionDeathsText, FormatNumber(stats.SessionDeaths));
        SetTextIfChanged(SessionSurvivedText, FormatDuration(stats.SessionTimeSurvived, "Waiting for join"));

        SetTextIfChanged(TotalCommandsText, FormatNumber(stats.TotalGameCommandsRun));
        SetTextIfChanged(TotalMostUsedCommandText, FormatName(stats.TotalMostUsedCommand));
        SetTextIfChanged(TotalTokensSpentText, FormatNumber(stats.TotalTokensSpent));
        SetTextIfChanged(TotalEffectsText, FormatNumber(stats.TotalEffectsGiven));
        SetTextIfChanged(TotalDangerousText, FormatName(stats.TotalMostDangerousViewer));
        SetTextIfChanged(TotalNicestText, FormatName(stats.TotalNicestViewer));
        SetTextIfChanged(TotalDeathsText, FormatNumber(stats.TotalDeaths));
        SetTextIfChanged(TotalLongestText, FormatDuration(stats.LongestTimeSurvived, "None"));
        SetTextIfChanged(TotalShortestText, FormatDuration(stats.ShortestTimeSurvived, "None"));
        SetTextIfChanged(TotalSessionsStartedText, FormatNumber(stats.SessionsStarted));
    }

    private static void SetTextIfChanged(TextBlock textBlock, string value)
    {
        if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
            textBlock.Text = value;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => AppHelpers.NavigateBack(this);

    private static string FormatName(string value) => string.IsNullOrWhiteSpace(value) ? "None yet" : value;

    private static string FormatNumber(long value) => Math.Max(0, value).ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan? duration, string fallback)
    {
        if (duration is not TimeSpan value)
            return fallback;

        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        long totalSeconds = value.Ticks / TimeSpan.TicksPerSecond;
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return hours.ToString(CultureInfo.InvariantCulture) + "h " +
                   minutes.ToString(CultureInfo.InvariantCulture) + "m " +
                   seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        if (minutes > 0)
        {
            return minutes.ToString(CultureInfo.InvariantCulture) + "m " +
                   seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        return seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }
}
