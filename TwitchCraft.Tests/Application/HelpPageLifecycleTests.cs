using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TwitchCraft_V1.Frames;

namespace TwitchCraft.Tests.Application;

public sealed class HelpPageLifecycleTests
{
    [Fact]
    public void CopyConfirmation_RestartsTimeoutAndClearsOnUnload()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Help help = new();
                TextBlock status = Assert.IsType<TextBlock>(help.FindName("DiagnosticsStatus"));
                FieldInfo timerField = typeof(Help).GetField(
                    "_diagnosticsTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                DispatcherTimer timer = Assert.IsType<DispatcherTimer>(timerField.GetValue(help));
                Assert.Equal(TimeSpan.FromSeconds(2), timer.Interval);

                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                DispatcherOperation? timerOperation = null;
                DispatcherHookEventHandler onPosted = (_, e) =>
                {
                    if (e.Operation.Priority == DispatcherPriority.Inactive)
                        timerOperation = e.Operation;
                };
                dispatcher.Hooks.OperationPosted += onPosted;
                try
                {
                    DispatcherOperation ShowConfirmation()
                    {
                        timerOperation = null;
                        help.ShowDiagnosticsCopied();
                        return Assert.IsType<DispatcherOperation>(timerOperation);
                    }

                    DispatcherOperation first = ShowConfirmation();
                    Assert.Equal("Copied to clipboard.", status.Text);

                    DispatcherOperation second = ShowConfirmation();
                    Assert.Equal(DispatcherOperationStatus.Aborted, first.Status);

                    second.Priority = DispatcherPriority.Background;
                    Assert.Equal(DispatcherOperationStatus.Completed, second.Wait());
                    Assert.Empty(status.Text);

                    DispatcherOperation third = ShowConfirmation();
                    help.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    Assert.Equal(DispatcherOperationStatus.Aborted, third.Status);
                    Assert.Empty(status.Text);

                    DispatcherOperation fourth = ShowConfirmation();
                    Assert.Equal("Copied to clipboard.", status.Text);
                    fourth.Priority = DispatcherPriority.Background;
                    Assert.Equal(DispatcherOperationStatus.Completed, fourth.Wait());
                    Assert.Empty(status.Text);
                }
                finally
                {
                    dispatcher.Hooks.OperationPosted -= onPosted;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Help confirmation test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
