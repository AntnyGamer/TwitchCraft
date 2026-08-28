using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TwitchCraftBot_V1.Frames;

namespace TwitchCraftBot.Tests.Application;

public sealed class SettingsAuthorizationStateTests
{
    [Fact]
    public void LeavingAndReopeningSettingsRestoresAuthorizeButtonImmediately()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Settings settings = new();
                Button authorizeButton = Assert.IsType<Button>(settings.FindName("AuthorizeTwitchButton"));
                authorizeButton.Content = "Waiting For Twitch...";
                authorizeButton.IsEnabled = false;

                settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.Equal("Authorize Twitch", authorizeButton.Content);
                Assert.True(authorizeButton.IsEnabled);

                authorizeButton.Content = "Waiting For Twitch...";
                authorizeButton.IsEnabled = false;
                settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Equal("Authorize Twitch", authorizeButton.Content);
                Assert.True(authorizeButton.IsEnabled);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Settings UI test did not finish.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
