using System.Collections.Concurrent;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Commands;

public sealed class TimedScaleLifecycleTests
{
    [Fact]
    public async Task ApplyAsync_SendsVersionCorrectScaleAndRestoresNormalSizeAfterDelay()
    {
        List<string> sentCommands = [];
        List<string> initialCommands = [];
        List<Task> trackedTasks = [];
        ConcurrentQueue<TaskCompletionSource> delays = [];
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        bool applied = await controller.ApplyAsync(
            ["PlayerOne"],
            0.5,
            usesModernAttributeIds: false,
            usesInlineTextComponents: false,
            TimeSpan.FromSeconds(30),
            (commands, _) =>
            {
                initialCommands.AddRange(commands);
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.True(applied);
        Assert.Equal(
            ["execute as @a[name=\"PlayerOne\",limit=1] run attribute @s minecraft:generic.scale base set 0.5"],
            initialCommands);
        _ = Assert.Single(trackedTasks);
        Assert.Single(delays);

        Assert.True(delays.TryDequeue(out TaskCompletionSource? warningDelay));
        warningDelay.SetResult();
        TaskCompletionSource resetDelay = await WaitForNextDelayAsync(delays);

        Assert.Equal(
            [
                "title @a[name=\"PlayerOne\",limit=1] times 0 60 0",
                "title @a[name=\"PlayerOne\",limit=1] subtitle {\"text\":\"RETURNING TO NORMAL SIZE IN 3 SECONDS!\",\"color\":\"red\",\"bold\":false}",
                "title @a[name=\"PlayerOne\",limit=1] title {\"text\":\" \",\"color\":\"white\",\"bold\":false}"
            ],
            sentCommands);

        resetDelay.SetResult();
        await trackedTasks[0];
        Assert.Equal(
            "execute as @a[name=\"PlayerOne\",limit=1] run attribute @s minecraft:generic.scale base set 1",
            sentCommands[^1]);
    }

    [Fact]
    public async Task ApplyAsync_NewerSizeChangeSupersedesTheOlderResetTimer()
    {
        List<string> sentCommands = [];
        List<Task> trackedTasks = [];
        ConcurrentQueue<TaskCompletionSource> delays = [];
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        Assert.True(await ApplySuccessfulAsync(controller, 0.5));
        Assert.True(await ApplySuccessfulAsync(controller, 2.0));
        Assert.Equal(2, trackedTasks.Count);
        Assert.Equal(2, delays.Count);

        Assert.True(delays.TryDequeue(out TaskCompletionSource? olderWarningDelay));
        olderWarningDelay.SetResult();
        await trackedTasks[0];
        Assert.Empty(sentCommands);

        Assert.True(delays.TryDequeue(out TaskCompletionSource? newerWarningDelay));
        newerWarningDelay.SetResult();
        TaskCompletionSource newerResetDelay = await WaitForNextDelayAsync(delays);
        Assert.Equal(3, sentCommands.Count);
        newerResetDelay.SetResult();
        await trackedTasks[1];
        Assert.Equal(4, sentCommands.Count);
        Assert.EndsWith("minecraft:scale base set 1", sentCommands[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotArmAResetWhenInitialDispatchFails()
    {
        List<string> sentCommands = [];
        List<Task> trackedTasks = [];
        ConcurrentQueue<TaskCompletionSource> delays = [];
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        bool applied = await controller.ApplyAsync(
            ["PlayerOne"],
            2.0,
            usesModernAttributeIds: true,
            usesInlineTextComponents: true,
            TimeSpan.FromSeconds(30),
            (_, _) => Task.FromResult(false),
            CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(trackedTasks);
        Assert.Empty(delays);
        Assert.Empty(sentCommands);
    }

    [Fact]
    public async Task ResetAllAsync_RestoresTrackedPlayersBeforeSessionShutdown()
    {
        List<string> sentCommands = [];
        List<Task> trackedTasks = [];
        ConcurrentQueue<TaskCompletionSource> delays = [];
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        Assert.True(await controller.ApplyAsync(
            ["Alice", "Bob"],
            2.0,
            usesModernAttributeIds: true,
            usesInlineTextComponents: true,
            TimeSpan.FromSeconds(30),
            (_, _) => Task.FromResult(true),
            CancellationToken.None));

        await controller.ResetAllAsync(CancellationToken.None);

        Assert.Equal(2, sentCommands.Count);
        Assert.All(sentCommands, command => Assert.EndsWith("minecraft:scale base set 1", command, StringComparison.Ordinal));
        while (delays.TryDequeue(out TaskCompletionSource? delay))
            delay.SetResult();
        await Task.WhenAll(trackedTasks);
        Assert.Equal(2, sentCommands.Count);
    }

    private static TimedPlayerScaleController CreateController(
        List<string> sentCommands,
        List<Task> trackedTasks,
        ConcurrentQueue<TaskCompletionSource> delays)
        => new(
            (command, _) =>
            {
                sentCommands.Add(command);
                return Task.FromResult(true);
            },
            trackedTasks.Add,
            _ => { },
            (_, cancellationToken) =>
            {
                TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                delays.Enqueue(completion);
                return completion.Task.WaitAsync(cancellationToken);
            });

    private static Task<bool> ApplySuccessfulAsync(TimedPlayerScaleController controller, double scale)
        => controller.ApplyAsync(
            ["PlayerOne"],
            scale,
            usesModernAttributeIds: true,
            usesInlineTextComponents: true,
            TimeSpan.FromSeconds(30),
            (_, _) => Task.FromResult(true),
            CancellationToken.None);

    private static async Task<TaskCompletionSource> WaitForNextDelayAsync(
        ConcurrentQueue<TaskCompletionSource> delays)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        TaskCompletionSource? delay;
        while (!delays.TryDequeue(out delay))
        {
            try
            {
                await Task.Delay(10, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Expected scale reset delay was not scheduled within 10 seconds.");
            }
        }

        return delay;
    }
}
