using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Commands;

public sealed class TimedScaleLifecycleTests
{
    [Fact]
    public async Task ApplyAsync_SendsVersionCorrectScaleAndRestoresAfterDelay()
    {
        List<string> sentCommands = [];
        List<string> initialCommands = [];
        List<Task> trackedTasks = [];
        Channel<TaskCompletionSource> delays = Channel.CreateUnbounded<TaskCompletionSource>();
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        bool applied = await controller.ApplyAsync(
            ["PlayerOne"],
            0.5,
            usesModernAttributeIDs: false,
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
        Assert.Equal(1, delays.Reader.Count);

        Assert.True(delays.Reader.TryRead(out TaskCompletionSource? warningDelay));
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
    public async Task ApplyAsync_NewerSizeChangeSupersedesOlderResetTimer()
    {
        List<string> sentCommands = [];
        List<Task> trackedTasks = [];
        Channel<TaskCompletionSource> delays = Channel.CreateUnbounded<TaskCompletionSource>();
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        Assert.True(await ApplySuccessfulAsync(controller, 0.5));
        Assert.True(await ApplySuccessfulAsync(controller, 2.0));
        Assert.Equal(2, trackedTasks.Count);
        Assert.Equal(2, delays.Reader.Count);

        Assert.True(delays.Reader.TryRead(out TaskCompletionSource? olderWarningDelay));
        olderWarningDelay.SetResult();
        await trackedTasks[0];
        Assert.Empty(sentCommands);

        Assert.True(delays.Reader.TryRead(out TaskCompletionSource? newerWarningDelay));
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
        Channel<TaskCompletionSource> delays = Channel.CreateUnbounded<TaskCompletionSource>();
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        bool applied = await controller.ApplyAsync(
            ["PlayerOne"],
            2.0,
            usesModernAttributeIDs: true,
            usesInlineTextComponents: true,
            TimeSpan.FromSeconds(30),
            (_, _) => Task.FromResult(false),
            CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(trackedTasks);
        Assert.Equal(0, delays.Reader.Count);
        Assert.Empty(sentCommands);
    }

    [Fact]
    public async Task ResetAllAsync_RestoresTrackedPlayersBeforeSessionShutdown()
    {
        List<string> sentCommands = [];
        List<Task> trackedTasks = [];
        Channel<TaskCompletionSource> delays = Channel.CreateUnbounded<TaskCompletionSource>();
        TimedPlayerScaleController controller = CreateController(sentCommands, trackedTasks, delays);

        Assert.True(await controller.ApplyAsync(
            ["Alice", "Bob"],
            2.0,
            usesModernAttributeIDs: true,
            usesInlineTextComponents: true,
            TimeSpan.FromSeconds(30),
            (_, _) => Task.FromResult(true),
            CancellationToken.None));

        await controller.ResetAllAsync(CancellationToken.None);

        Assert.Equal(2, sentCommands.Count);
        Assert.All(sentCommands, command => Assert.EndsWith("minecraft:scale base set 1", command, StringComparison.Ordinal));
        while (delays.Reader.TryRead(out TaskCompletionSource? delay))
            delay.SetResult();
        await Task.WhenAll(trackedTasks);
        Assert.Equal(2, sentCommands.Count);
    }

    private static TimedPlayerScaleController CreateController(
        List<string> sentCommands,
        List<Task> trackedTasks,
        Channel<TaskCompletionSource> delays)
        => new(
            (command, _) =>
            {
                sentCommands.Add(command);
                return Task.FromResult(true);
            },
            (commands, _) =>
            {
                sentCommands.AddRange(commands);
                return Task.FromResult(true);
            },
            _ => true,
            trackedTasks.Add,
            _ => { },
            (_, cancellationToken) =>
            {
                TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.True(delays.Writer.TryWrite(completion));
                return completion.Task.WaitAsync(cancellationToken);
            });

    private static Task<bool> ApplySuccessfulAsync(TimedPlayerScaleController controller, double scale)
        => controller.ApplyAsync(
            ["PlayerOne"],
            scale,
            usesModernAttributeIDs: true,
            usesInlineTextComponents: true,
            TimeSpan.FromSeconds(30),
            (_, _) => Task.FromResult(true),
            CancellationToken.None);

    private static async Task<TaskCompletionSource> WaitForNextDelayAsync(
        Channel<TaskCompletionSource> delays)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            return await delays.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Expected scale reset delay was not scheduled within 10 seconds.");
        }
    }
}
