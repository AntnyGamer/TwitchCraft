using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TwitchCraftBot_V1.Frames;

namespace TwitchCraftBot.Tests.Revamped.Authentication;

public sealed class AuthorizationUiLifecycleTests
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
                AssertAuthorizationIsReady(authorizeButton);

                authorizeButton.Content = "Waiting For Twitch...";
                authorizeButton.IsEnabled = false;
                settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                AssertAuthorizationIsReady(authorizeButton);
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
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Settings UI test did not finish.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void AssertAuthorizationIsReady(Button authorizeButton)
    {
        Assert.True(authorizeButton.IsEnabled);
        Assert.Contains(
            Assert.IsType<string>(authorizeButton.Content),
            new[] { "Authorize Twitch", "Reauthorize Twitch" });
    }
}
