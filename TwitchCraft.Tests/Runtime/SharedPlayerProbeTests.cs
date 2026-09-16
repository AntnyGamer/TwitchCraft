using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class SharedPlayerProbeTests
{
    [Fact]
    public async Task QueryPlayerProbe_CancelingOneCallerDoesNotCancelTheSharedProbe()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        MethodInfo query = (typeof(MainHandler).GetMethod(
            "QueryPlayerAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Player probe method was not found."))
            .MakeGenericMethod(typeof(string));
        Lock gate = new();
        Dictionary<string, TaskCompletionSource<string?>> pending = new(StringComparer.OrdinalIgnoreCase);
        TaskCompletionSource<bool> sendStarted = CreateSignal();
        TaskCompletionSource<bool> releaseSend = CreateSignal();
        Func<Action, CancellationToken, Task<bool>> sendProbe = async (_, cancellationToken) =>
        {
            sendStarted.TrySetResult(true);
            await releaseSend.Task.WaitAsync(cancellationToken);
            return true;
        };
        using CancellationTokenSource firstCaller = new();

        try
        {
#pragma warning disable CS9216
            Task<string?> canceledTask = (Task<string?>)query.Invoke(
                runtime,
                ["PlayerOne", gate, pending, sendProbe, firstCaller.Token])!;
            Task<string?> survivingTask = (Task<string?>)query.Invoke(
                runtime,
                ["PlayerOne", gate, pending, sendProbe, CancellationToken.None])!;
#pragma warning restore CS9216

            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), testCancellation);
            firstCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);

            TaskCompletionSource<string?> sharedWaiter = pending["PlayerOne"];
            MainHandler.CompletePlayer("PlayerOne", gate, pending, sharedWaiter, "diamond");
            releaseSend.TrySetResult(true);

            Assert.Equal("diamond", await survivingTask.WaitAsync(TimeSpan.FromSeconds(10), testCancellation));
            Assert.Empty(pending);
        }
        finally
        {
            releaseSend.TrySetResult(true);
            runtime.Tokens.Close();
        }
    }

    private static TaskCompletionSource<bool> CreateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
