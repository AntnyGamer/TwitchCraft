using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Twitch;

public sealed class IrcQueueConcurrencyTests
{
    [Fact]
    public async Task QueueCommand_ProcessesItemsInFifoOrderWithOneActiveWorker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = CreateRuntime(directory.Path);
        TaskCompletionSource<bool> firstStarted = CreateSignal();
        TaskCompletionSource<bool> releaseFirst = CreateSignal();
        TaskCompletionSource<bool> allCompleted = CreateSignal();
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

                if (Interlocked.Increment(ref completed) == 3)
                    allCompleted.TrySetResult(true);
            }
        };

        try
        {
            Assert.True(runtime.QueueCommand(CreateWork(1), "!one", cancellationToken));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.True(runtime.QueueCommand(CreateWork(2), "!two", cancellationToken));
            Assert.True(runtime.QueueCommand(CreateWork(3), "!three", cancellationToken));

            releaseFirst.TrySetResult(true);
            await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            Assert.Equal([1, 2, 3], order);
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
        BotMainHandler runtime = CreateRuntime(directory.Path);
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
    public async Task ResetQueues_DropsQueuedOldWorkAndSerializesNewGenerationAfterRunningWork()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = CreateRuntime(directory.Path);
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

    private static BotMainHandler CreateRuntime(string directory)
        => new(
            new AppShellViewModel(),
            System.IO.Path.Combine(directory, "viewer_tokens.db"));

    private static TaskCompletionSource<bool> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
