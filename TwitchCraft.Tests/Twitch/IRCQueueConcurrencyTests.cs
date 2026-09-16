using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

public sealed class IRCQueueConcurrencyTests
{
    [Fact]
    public async Task QueueCommand_EnforcesCapacityThenRecoversWithFifoAndOneActiveWorker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        MainHandler runtime = CreateRuntime(directory.Path);
        TaskCompletionSource<bool> firstStarted = CreateSignal();
        TaskCompletionSource<bool> releaseFirst = CreateSignal();
        TaskCompletionSource<bool> allCompleted = CreateSignal();
        TaskCompletionSource<bool> recovered = CreateSignal();
        int capacity = runtime.MaxGameplayCommandQueue;
        Lock stateGate = new();
        List<int> order = [];
        int activeWorkers = 0;
        int maxActiveWorkers = 0;
        int completed = 0;

        Func<CancellationToken, Task> CreateWork(int item) => async token =>
        {
            lock (stateGate)
            {
                activeWorkers++;
                maxActiveWorkers = Math.Max(maxActiveWorkers, activeWorkers);
                order.Add(item);
            }

            try
            {
                if (item == 1)
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(token);
                }
            }
            finally
            {
                lock (stateGate)
                    activeWorkers--;

                if (Interlocked.Increment(ref completed) == capacity)
                    allCompleted.TrySetResult(true);
            }
        };

        try
        {
            Assert.True(runtime.QueueCommand(CreateWork(1), "!one", cancellationToken));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            for (int item = 2; item <= capacity; item++)
                Assert.True(runtime.QueueCommand(CreateWork(item), "!queued", cancellationToken));
            Assert.False(runtime.QueueCommand(CreateWork(capacity + 1), "!overflow", cancellationToken));

            releaseFirst.TrySetResult(true);
            await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            Assert.True(runtime.QueueCommand(_ =>
            {
                recovered.TrySetResult(true);
                return Task.CompletedTask;
            }, "!recovered", cancellationToken));
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.Equal(Enumerable.Range(1, capacity), order);
            Assert.Equal(1, maxActiveWorkers);
        }
        finally
        {
            runtime.ResetQueues();
            releaseFirst.TrySetResult(true);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task QueueCommand_ContinuesAfterAnItemThrows()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        MainHandler runtime = CreateRuntime(directory.Path);
        TaskCompletionSource<bool> firstStarted = CreateSignal();
        TaskCompletionSource<bool> releaseFirst = CreateSignal();
        TaskCompletionSource<bool> secondCompleted = CreateSignal();

        try
        {
            Assert.True(runtime.QueueCommand(
                async token =>
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(token);
                    throw new InvalidOperationException("expected test failure");
                },
                "!first",
                cancellationToken));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.True(runtime.QueueCommand(
                _ =>
                {
                    secondCompleted.TrySetResult(true);
                    return Task.CompletedTask;
                },
                "!second",
                cancellationToken));

            releaseFirst.TrySetResult(true);

            await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        finally
        {
            runtime.ResetQueues();
            releaseFirst.TrySetResult(true);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task ResetQueues_DropsOldQueuedWorkAndSerializesNewWorkAfterRunningWork()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        MainHandler runtime = CreateRuntime(directory.Path);
        TaskCompletionSource<bool> oldWorkStarted = CreateSignal();
        TaskCompletionSource<bool> releaseOldWork = CreateSignal();
        TaskCompletionSource<bool> oldWorkCompleted = CreateSignal();
        TaskCompletionSource<bool> newWorkCompleted = CreateSignal();
        Lock orderGate = new();
        List<string> order = [];
        int rejectedWorkRuns = 0;

        try
        {
            Assert.True(runtime.QueueCommand(
                async token =>
                {
                    lock (orderGate)
                        order.Add("old-start");

                    oldWorkStarted.TrySetResult(true);
                    try
                    {
                        await releaseOldWork.Task.WaitAsync(token);
                    }
                    finally
                    {
                        lock (orderGate)
                            order.Add("old-end");

                        oldWorkCompleted.TrySetResult(true);
                    }
                },
                "!old-running",
                cancellationToken));
            await oldWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            Assert.True(runtime.QueueCommand(
                _ =>
                {
                    Interlocked.Increment(ref rejectedWorkRuns);
                    return Task.CompletedTask;
                },
                "!old-queued",
                cancellationToken));

            runtime.ResetQueues();

            Assert.True(runtime.QueueCommand(
                _ =>
                {
                    lock (orderGate)
                        order.Add("new-start");

                    lock (orderGate)
                        order.Add("new-end");

                    newWorkCompleted.TrySetResult(true);
                    return Task.CompletedTask;
                },
                "!new",
                cancellationToken));

            releaseOldWork.TrySetResult(true);
            await Task.WhenAll(
                oldWorkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken),
                newWorkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));

            Assert.Equal(0, Volatile.Read(ref rejectedWorkRuns));
            Assert.Equal(["old-start", "old-end", "new-start", "new-end"], order);
        }
        finally
        {
            runtime.ResetQueues();
            releaseOldWork.TrySetResult(true);
            runtime.Tokens.Close();
        }
    }

    private static MainHandler CreateRuntime(string directory)
        => new(
            new AppShellViewModel(),
            System.IO.Path.Combine(directory, "viewer_tokens.db"));

    private static TaskCompletionSource<bool> CreateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
