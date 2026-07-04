using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;

namespace TwitchCraftBot_V1;

internal static class UIThread
{
    public static void Invoke(Action action)
    {
        if (action == null || !TryGetDispatcher(out Dispatcher? dispatcher))
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action, DispatcherPriority.Normal);
        }
    }

    public static void BeginInvoke(Action action)
    {
        if (action == null || !TryGetDispatcher(out Dispatcher? dispatcher))
        {
            return;
        }

        dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    public static MessageBoxResult ShowMessageBox(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        MessageBoxResult result = MessageBoxResult.None;

        Invoke(() =>
        {
            string message = text ?? string.Empty;
            string title = caption ?? "TwitchCraftBot";
            Window? safeOwner = owner;

            if (safeOwner != null)
            {
                Dispatcher ownerDispatcher = safeOwner.Dispatcher;
                if (ownerDispatcher.HasShutdownStarted || ownerDispatcher.HasShutdownFinished)
                {
                    safeOwner = null;
                }
            }

            if (safeOwner != null && safeOwner.IsLoaded && safeOwner.Visibility == Visibility.Visible)
            {
                result = MessageBox.Show(safeOwner, message, title, buttons, image);
            }
            else
            {
                result = MessageBox.Show(message, title, buttons, image);
            }
        });

        return result;
    }

    private static bool TryGetDispatcher([NotNullWhen(true)] out Dispatcher? dispatcher)
    {
        dispatcher = Application.Current?.Dispatcher;
        return dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished;
    }
}
