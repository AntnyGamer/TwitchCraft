using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, params string[] affectedProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged();

        for (int i = 0; i < affectedProperties.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(affectedProperties[i]))
            {
                OnPropertyChanged(affectedProperties[i]);
            }
        }

        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AppShellViewModel : ObservableObject
{
    private static readonly string[] CurrentPageAffectedProperties =
    [
        nameof(IsSetupVisible),
        nameof(IsLaunchVisible),
        nameof(IsConsoleVisible),
        nameof(IsHelpVisible),
        nameof(IsSettingsVisible),
        nameof(IsStatisticsVisible)
    ];

    private ShellPage _currentPage;
    private ShellPage _previousPage;
    private ShellPage _pageBeforeSettings;
    private ShellPage _helpBackTarget;

    public ShellPage PreviousPage
    {
        get => _previousPage;
        private set => SetProperty(ref _previousPage, value);
    }

    public ShellPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            SetProperty(ref _currentPage, value, CurrentPageAffectedProperties);
        }
    }

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
    }
}
