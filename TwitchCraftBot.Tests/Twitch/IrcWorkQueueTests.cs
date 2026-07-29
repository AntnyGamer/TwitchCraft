using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Twitch;

public sealed class IrcWorkQueueTests
{
    [Fact]
    public async Task QueueIRCCommandWork_ProcessesItemsInFifoOrderWithOneActiveWorker()
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

        Assert.True(runtime.QueueIRCCommandWork(CreateWork(1), "!one", cancellationToken));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.True(runtime.QueueIRCCommandWork(CreateWork(2), "!two", cancellationToken));
        Assert.True(runtime.QueueIRCCommandWork(CreateWork(3), "!three", cancellationToken));

        releaseFirst.TrySetResult(true);
        await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        Assert.Equal([1, 2, 3], order);
        Assert.Equal(1, maxActiveWorkers);
    }

    [Fact]
    public async Task QueueIRCCommandWork_ContinuesAfterAnItemThrows()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = CreateRuntime(directory.Path);
        TaskCompletionSource<bool> firstStarted = CreateSignal();
        TaskCompletionSource<bool> releaseFirst = CreateSignal();
        TaskCompletionSource<bool> secondCompleted = CreateSignal();

        Assert.True(runtime.QueueIRCCommandWork(
            async token =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task.WaitAsync(token);
                throw new InvalidOperationException("expected test failure");
            },
            "!first",
            cancellationToken));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.True(runtime.QueueIRCCommandWork(
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

    [Fact]
    public async Task ResetIRCQueues_RejectsQueuedWorkFromTheOldGeneration()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = CreateRuntime(directory.Path);
        TaskCompletionSource<bool> oldWorkStarted = CreateSignal();
        TaskCompletionSource<bool> releaseOldWork = CreateSignal();
        TaskCompletionSource<bool> oldWorkCompleted = CreateSignal();
        TaskCompletionSource<bool> newWorkCompleted = CreateSignal();
        int rejectedWorkRuns = 0;

        Assert.True(runtime.QueueIRCCommandWork(
            async token =>
            {
                oldWorkStarted.TrySetResult(true);
                try
                {
                    await releaseOldWork.Task.WaitAsync(token);
                }
                finally
                {
                    oldWorkCompleted.TrySetResult(true);
                }
            },
            "!old-running",
            cancellationToken));
        await oldWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.True(runtime.QueueIRCCommandWork(
            _ =>
            {
                Interlocked.Increment(ref rejectedWorkRuns);
                return Task.CompletedTask;
            },
            "!old-queued",
            cancellationToken));

        runtime.ResetIRCQueues();
        Assert.True(runtime.QueueIRCCommandWork(
            _ =>
            {
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
    }

    private static BotMainHandler CreateRuntime(string directory)
        => new(
            new AppShellViewModel(),
            System.IO.Path.Combine(directory, "viewer_tokens.db"),
            initializeApplicationState: false);

    private static TaskCompletionSource<bool> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
