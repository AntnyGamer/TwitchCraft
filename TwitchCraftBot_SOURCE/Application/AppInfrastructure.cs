using System.ComponentModel;

namespace TwitchCraftBot_V1;

public enum ShellPage
{
    Setup,
    Start,
    Main,
    Help,
    Settings,
    Statistics
}

public sealed class AppShellViewModel : INotifyPropertyChanged
{
    private ShellPage _pageBeforeSettings;
    private ShellPage _helpBackTarget;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellPage PreviousPage { get; private set; }

    public ShellPage CurrentPage { get; private set; }

    public bool IsSetupVisible => CurrentPage == ShellPage.Setup;
    public bool IsLaunchVisible => CurrentPage == ShellPage.Start;
    public bool IsConsoleVisible => CurrentPage == ShellPage.Main;
    public bool IsHelpVisible => CurrentPage == ShellPage.Help;
    public bool IsSettingsVisible => CurrentPage == ShellPage.Settings;
    public bool IsStatisticsVisible => CurrentPage == ShellPage.Statistics;

    public void Navigate(ShellPage page)
    {
        if (CurrentPage == page)
        {
            return;
        }

        if (page == ShellPage.Settings)
        {
            _pageBeforeSettings = CurrentPage;

            if (CurrentPage == ShellPage.Help)
            {
                _helpBackTarget = PreviousPage;
            }
        }

        if (CurrentPage == ShellPage.Settings &&
            page == ShellPage.Help &&
            _pageBeforeSettings == ShellPage.Help)
        {
            PreviousPage = _helpBackTarget;
        }
        else
        {
            PreviousPage = CurrentPage;
        }

        CurrentPage = page;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPage)));
    }
}
