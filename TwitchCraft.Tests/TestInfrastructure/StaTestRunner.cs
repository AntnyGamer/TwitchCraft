using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using Xunit;

namespace TwitchCraft.Tests.TestInfrastructure;

internal static class StaTestRunner
{
    internal static void Run(Action body, TimeSpan timeout, string timeoutMessage)
    {
        ArgumentNullException.ThrowIfNull(body);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                body();
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
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(timeout), timeoutMessage);
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
