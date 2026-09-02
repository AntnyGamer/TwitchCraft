using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

internal sealed class BackgroundTaskTracker
{
    private readonly Lock _gate = new();
    private readonly List<Task> _tasks = [];

    internal void Track(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.IsCompleted)
        {
            if (task.IsFaulted)
                ErrorHandling.LogNonFatal("Background task failed", task.Exception);
            return;
        }

        lock (_gate)
            _tasks.Add(task);
        _ = task.ContinueWith(
            completedTask =>
            {
                if (completedTask.IsFaulted)
                    ErrorHandling.LogNonFatal("Background task failed", completedTask.Exception);

                lock (_gate)
                    _tasks.Remove(completedTask);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal Task[] Snapshot()
    {
        lock (_gate)
            return [.. _tasks];
    }

    internal void Clear()
    {
        lock (_gate)
            _tasks.Clear();
    }
}
