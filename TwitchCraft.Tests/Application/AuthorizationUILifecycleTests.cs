using System;
using System.Windows;
using System.Windows.Controls;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1.Frames;
using Xunit;

namespace TwitchCraft.Tests.Application;

public sealed class AuthorizationUILifecycleTests
{
    [Fact]
    public void SettingsReopen_ClearsCredentialsAndRestoresAuthorization()
    {
        StaTestRunner.Run(() =>
        {
            Settings settings = new();
            Button authorizeButton = Assert.IsType<Button>(settings.FindName("AuthorizeTwitchButton"));
            PasswordBox password = Assert.IsType<PasswordBox>(settings.FindName("RCONPasswordBox"));
            TextBox visiblePassword = Assert.IsType<TextBox>(settings.FindName("RCONPasswordTextBox"));
            Button showPassword = Assert.IsType<Button>(settings.FindName("RCONPasswordShowButton"));
            authorizeButton.Content = "Waiting For Twitch...";
            authorizeButton.IsEnabled = false;
            authorizeButton.ToolTip = "Authorization pending";
            password.Password = "test credential";
            visiblePassword.Text = "test credential";
            password.Visibility = Visibility.Collapsed;
            visiblePassword.Visibility = Visibility.Visible;
            showPassword.Content = "Hide";

            settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            AssertAuthorizationIsReady(authorizeButton);
            Assert.Empty(password.Password);
            Assert.Empty(visiblePassword.Text);
            Assert.Equal(Visibility.Visible, password.Visibility);
            Assert.Equal(Visibility.Collapsed, visiblePassword.Visibility);
            Assert.Equal("Show", showPassword.Content);

            authorizeButton.Content = "Waiting For Twitch...";
            authorizeButton.IsEnabled = false;
            settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            AssertAuthorizationIsReady(authorizeButton);
        }, TimeSpan.FromSeconds(15), "Settings UI test did not finish.");
    }

    private static void AssertAuthorizationIsReady(Button authorizeButton)
    {
        Assert.True(authorizeButton.IsEnabled);
        Assert.Null(authorizeButton.ToolTip);
        Assert.Contains(
            Assert.IsType<string>(authorizeButton.Content),
            [ "Authorize Twitch", "Reauthorize Twitch" ]);
    }
}
