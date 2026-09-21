using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

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
            static (completedTask, state) => ((BackgroundTaskTracker)state!).Complete(completedTask),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Complete(Task task)
    {
        if (task.IsFaulted) ErrorHandling.LogNonFatal("Background task failed", task.Exception);
        lock (_gate) _tasks.Remove(task);
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
