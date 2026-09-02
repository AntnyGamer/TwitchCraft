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
        _refreshTimer.Tick += Refresh_Tick;
        Loaded += (_, _) => StartRefreshTimer();
        Unloaded += (_, _) => StopRefreshTimer(clearParent: true);
        IsVisibleChanged += (_, _) => { if (IsVisible) StartRefreshTimer(); else StopRefreshTimer(clearParent: false); };
    }

    private void StartRefreshTimer()
    {
        if (!IsVisible || _refreshTimer.IsEnabled || _refreshing)
            return;

        _refreshTimer.Start();
        Refresh_Tick(this, EventArgs.Empty);
    }

    private void StopRefreshTimer(bool clearParent)
    {
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
        if (clearParent)
            _parentBot = null;
    }

    private async void Refresh_Tick(object? sender, EventArgs e)
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
            _parentBot ??= AppHelpers.GetBotWindow(this);
            if (_parentBot is not TwitchCraftBot parent || !IsVisible)
                return;

            BotStatisticsSnapshot? stats = await Task.Run(() => parent.Runtime.Statistics.GetSnapshot(refreshToken), refreshToken);
            if (stats != null && IsVisible && !refreshToken.IsCancellationRequested)
                UpdateStats(stats);
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

    private void UpdateStats(BotStatisticsSnapshot stats)
    {
        Visibility disabledVisibility = stats.StatisticsEnabled ? Visibility.Collapsed : Visibility.Visible;
        if (StatisticsDisabledBox.Visibility != disabledVisibility)
            StatisticsDisabledBox.Visibility = disabledVisibility;

        SetText(SessionCommandsText, FormatNumber(stats.SessionGameCommandsRun));
        SetText(SessionMostUsedCommandText, FormatName(stats.SessionMostUsedCommand));
        SetText(SessionTokensSpentText, FormatNumber(stats.SessionTokensSpent));
        SetText(SessionEffectsText, FormatNumber(stats.SessionEffectsGiven));
        SetText(SessionDangerousText, FormatName(stats.SessionMostDangerousViewer));
        SetText(SessionNicestText, FormatName(stats.SessionNicestViewer));
        SetText(SessionDeathsText, FormatNumber(stats.SessionDeaths));
        SetText(SessionSurvivedText, FormatDuration(stats.SessionTimeSurvived, "Waiting for join"));

        SetText(TotalCommandsText, FormatNumber(stats.TotalGameCommandsRun));
        SetText(TotalMostUsedCommandText, FormatName(stats.TotalMostUsedCommand));
        SetText(TotalTokensSpentText, FormatNumber(stats.TotalTokensSpent));
        SetText(TotalEffectsText, FormatNumber(stats.TotalEffectsGiven));
        SetText(TotalDangerousText, FormatName(stats.TotalMostDangerousViewer));
        SetText(TotalNicestText, FormatName(stats.TotalNicestViewer));
        SetText(TotalDeathsText, FormatNumber(stats.TotalDeaths));
        SetText(TotalLongestText, FormatDuration(stats.LongestTimeSurvived, "None"));
        SetText(TotalShortestText, FormatDuration(stats.ShortestTimeSurvived, "None"));
        SetText(TotalSessionsStartedText, FormatNumber(stats.SessionsStarted));
    }

    private static void SetText(TextBlock textBlock, string value)
    {
        if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
            textBlock.Text = value;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => AppHelpers.NavigateBack(this);

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
            return string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m {seconds}s");

        if (minutes > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{minutes}m {seconds}s");

        return string.Create(CultureInfo.InvariantCulture, $"{seconds}s");
    }
}
