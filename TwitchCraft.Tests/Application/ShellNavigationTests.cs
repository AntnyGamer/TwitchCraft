using System;
using System.Collections.Generic;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Application;

public sealed class ShellNavigationTests
{
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
    public void Navigate_SettingsFromHelpPreservesBackTarget()
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
    public void Navigate_RaisesCurrentPageOnlyWhenPageChanges()
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
    public void EveryPage_ShowsOnlyItsMatchingFrame()
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
