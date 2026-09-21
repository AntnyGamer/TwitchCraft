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
            MainHandler.CompleteRequest("PlayerOne", gate, pending, sharedWaiter, "diamond");
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

    [Fact]
    public void HealthProbes_ParseVanillaFeedback()
    {
        Assert.True(MainHandler.TryParseMaxHealthResponse("Value of attribute Max Health for entity PlayerOne is 20.0", "PlayerOne", out double health));
        Assert.Equal(20, health);
        Assert.True(MainHandler.TryParseMaxHealthResponse("The value of attribute Max Health for entity [VIP] PlayerOne is 30.0", "PlayerOne", out health));
        Assert.Equal(30, health);
        Assert.True(MainHandler.TryParseMaxHealthResponse("Value of attribute minecraft:max_health for entity PlayerOne is 40", "PlayerOne", out health));
        Assert.Equal(40, health);
        Assert.False(MainHandler.TryParseMaxHealthResponse("Value of attribute Max Health for entity PlayerOne2 is 20", "PlayerOne", out _));
        Assert.True(MainHandler.TryParseHealthModifierResponse("Value of modifier twitchcraft:heart_1 on attribute Max Health for entity PlayerOne is -4.0", "PlayerOne", "twitchcraft:heart_1", out bool exists));
        Assert.True(exists);
        Assert.True(MainHandler.TryParseHealthModifierResponse("Attribute Max Health for entity [VIP] PlayerOne has no modifier twitchcraft:heart_1", "PlayerOne", "twitchcraft:heart_1", out exists));
        Assert.False(exists);
        Assert.False(MainHandler.TryParseHealthModifierResponse("Value of modifier twitchcraft:heart_1 on attribute Max Health for entity PlayerOne2 is -4.0", "PlayerOne", "twitchcraft:heart_1", out _));
    }

    private static TaskCompletionSource<bool> CreateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
