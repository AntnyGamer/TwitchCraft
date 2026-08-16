using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Application;

public sealed class AppShellViewModelTests
{
    [Fact]
    public void InitialState_ShowsSetupOnly()
    {
        AppShellViewModel shell = new();

        Assert.Equal(ShellPage.Setup, shell.CurrentPage);
        Assert.True(shell.IsSetupVisible);
        Assert.False(shell.IsLaunchVisible);
        Assert.False(shell.IsConsoleVisible);
        Assert.False(shell.IsHelpVisible);
        Assert.False(shell.IsSettingsVisible);
        Assert.False(shell.IsStatisticsVisible);
    }

    [Fact]
    public void Navigate_UpdatesCurrentPreviousAndVisibility()
    {
        AppShellViewModel shell = new();

        shell.Navigate(ShellPage.Start);
        shell.Navigate(ShellPage.Main);

        Assert.Equal(ShellPage.Main, shell.CurrentPage);
        Assert.Equal(ShellPage.Start, shell.PreviousPage);
        Assert.True(shell.IsConsoleVisible);
        Assert.False(shell.IsLaunchVisible);
    }

    [Fact]
    public void SettingsOpenedFromHelp_PreservesTheOriginalHelpBackTarget()
    {
        AppShellViewModel shell = new();
        shell.Navigate(ShellPage.Start);
        shell.Navigate(ShellPage.Help);
        shell.Navigate(ShellPage.Settings);

        shell.Navigate(ShellPage.Help);

        Assert.Equal(ShellPage.Help, shell.CurrentPage);
        Assert.Equal(ShellPage.Start, shell.PreviousPage);
    }

    [Fact]
    public void Navigate_RaisesCurrentPageOnceAndNothingForTheCurrentPage()
    {
        AppShellViewModel shell = new();
        List<string?> changed = [];
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.Navigate(ShellPage.Start);
        changed.Clear();
        shell.Navigate(ShellPage.Main);

        Assert.Equal([nameof(AppShellViewModel.CurrentPage)], changed);

        changed.Clear();
        shell.Navigate(ShellPage.Main);
        Assert.Empty(changed);
    }

    [Fact]
    public void EveryPage_ShowsExactlyItsMatchingFrame()
    {
        AppShellViewModel shell = new();

        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            shell.Navigate(page);

            bool[] visibility =
            [
                shell.IsSetupVisible,
                shell.IsLaunchVisible,
                shell.IsConsoleVisible,
                shell.IsHelpVisible,
                shell.IsSettingsVisible,
                shell.IsStatisticsVisible
            ];
            Assert.Equal(page, shell.CurrentPage);
            Assert.Single(visibility, visible => visible);
            Assert.True(visibility[(int)page]);
        }
    }
}
