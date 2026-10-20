using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1.Frames;
using Xunit;

namespace TwitchCraft.Tests.Application;

public sealed class SettingsCategoryNavigationTests
{
    [Fact]
    public void CategoryButtons_SelectOnlyTheirMatchingPage()
    {
        StaTestRunner.Run(() =>
        {
            Settings settings = new();
            string[] categories =
            [
                "Commands", "CustomCommands", "Gameplay", "Economy",
                "ChatDisplay", "Performance", "Server", "Dangerous"
            ];
            var controls = categories.Select(name => (
                Page: Assert.IsType<Grid>(settings.FindName($"{name}SettingsPage")),
                Button: Assert.IsType<Button>(settings.FindName($"{name}CategoryButton")))).ToArray();

            foreach (var (selectedPage, selectedButton) in controls)
            {
                selectedButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                foreach (var (page, button) in controls)
                {
                    bool selected = ReferenceEquals(page, selectedPage);
                    Assert.Equal(selected ? Visibility.Visible : Visibility.Collapsed, page.Visibility);
                    Assert.Equal(selected ? FontWeights.Bold : FontWeights.Normal, button.FontWeight);
                    Assert.Equal(selected ? 1.0 : 0.78, button.Opacity);
                }
            }
        }, TimeSpan.FromSeconds(15), "Settings category navigation test timed out.");
    }

    [Fact]
    public void PresetDropdowns_KeepTheirExpectedOrder()
    {
        StaTestRunner.Run(() =>
        {
            Settings settings = new();
            string[] prefixes = ["!", "?", ".", "#", "$", "%", "&", "+", "-", "~"];
            Assert.Equal(prefixes, Items("CommandPrefixTextBox"));
            Assert.Equal(prefixes, Items("SecondaryCommandPrefixTextBox"));
            Assert.Equal(["Normal", "Reduced", "Essential Only"], Items("ResponseVerbosityDropdown"));
            Assert.Equal(["25", "50", "100", "200", "500", "1000"], Items("FollowRewardAmountDropdown"));

            string[] Items(string name)
                => Assert.IsType<ComboBox>(settings.FindName(name)).Items.Cast<string>().ToArray();
        }, TimeSpan.FromSeconds(15), "Settings preset dropdown test timed out.");
    }
}
