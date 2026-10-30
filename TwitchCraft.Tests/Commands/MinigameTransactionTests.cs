using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TwitchCraft.Tests.Economy;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class MinigameTransactionTests
{
    [Fact]
    public async Task ReplyBetErrorAsync_ReportsRejectedReasonsWithoutSendingForSuccess()
    {
        List<string> messages = [];
        Func<string, CancellationToken, Task> capture = (message, _) =>
        {
            messages.Add(message);
            return Task.CompletedTask;
        };

        foreach (MinigameManager.BetUpdateResult result in new[]
        {
            MinigameManager.BetUpdateResult.NotEnoughTokens,
            MinigameManager.BetUpdateResult.OverMax,
            MinigameManager.BetUpdateResult.Closed
        })
            Assert.True(await MinigameManager.ReplyBetErrorAsync(
                result, "viewer", "chicken run", "betting is closed.", capture, CancellationToken.None), result.ToString());

        bool handled = await MinigameManager.ReplyBetErrorAsync(
            MinigameManager.BetUpdateResult.Updated,
            "viewer",
            "chicken run",
            "betting is closed.",
            capture,
            CancellationToken.None);
        Assert.False(handled);

        Assert.Equal(
            [
                "viewer, you do not have enough tokens for that bet.",
                "viewer, the max chicken run bet is 200 tokens.",
                "viewer, betting is closed."
            ],
            messages);
    }

    [Fact]
    public void FailedMinigameSettlement_RetriesWinningsInsteadOfRefundingStake()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "viewer_tokens.db");
        MainHandler runtime = new(new AppShellViewModel(), path);
        Type manager = typeof(MinigameManager);
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo start = Assert.IsAssignableFrom<MethodInfo>(manager.GetMethod("TryStart", flags));
        MethodInfo getState = Assert.IsAssignableFrom<MethodInfo>(manager.GetMethod("GetChickenStateNoLock", flags));
        MethodInfo settle = Assert.IsAssignableFrom<MethodInfo>(manager.GetMethod("SettleBets", flags));
        MethodInfo refund = Assert.IsAssignableFrom<MethodInfo>(manager.GetMethod("RefundBets", flags));

        runtime.Tokens.Load(0);
        try
        {
            runtime.Tokens.Award("winner", 100);
            object?[] args = [runtime, "ChickenRun", 0L];
            Assert.True((bool)start.Invoke(null, args)!);
            long roundID = (long)args[2]!;
            object state = getState.Invoke(null, [runtime])!;
            Type betType = manager.GetNestedType("ChickenRunBet", BindingFlags.NonPublic)!;
            object bet = Activator.CreateInstance(betType, nonPublic: true)!;
            betType.GetProperty("Viewer")!.SetValue(bet, "winner");
            betType.GetProperty("TokenAmount")!.SetValue(bet, 25);
            ((IList)state.GetType().GetProperty("Bets")!.GetValue(state)!).Add(bet);
            Assert.True(runtime.Tokens.TrySpendForMinigameBet("winner", 25));

            using SqliteConnection connection = new($"Data Source={path}");
            connection.Open();
            using SqliteCommand sql = connection.CreateCommand();
            sql.CommandText = "CREATE TRIGGER fail_settlement BEFORE UPDATE ON TokenBalances BEGIN SELECT RAISE(ABORT, 'test'); END;";
            sql.ExecuteNonQuery();

            List<KeyValuePair<string, int>> payouts = [new("winner", 50)];
            Assert.False((bool)settle.MakeGenericMethod(betType).Invoke(null, [runtime, state, "ChickenRun", roundID, payouts])!);
            Assert.Equal(75, runtime.Tokens.GetBalance("winner"));
            sql.CommandText = "DROP TRIGGER fail_settlement;";
            sql.ExecuteNonQuery();

            refund.MakeGenericMethod(betType).Invoke(null, [runtime, state]);
            Assert.Equal(125, runtime.Tokens.GetBalance("winner"));
            Assert.Empty((IList)state.GetType().GetProperty("Bets")!.GetValue(state)!);
        }
        finally
        {
            MinigameManager.StopLoops(runtime);
            runtime.Tokens.Close();
        }
    }

    [Theory]
    [InlineData("chickenbet", null, null, "viewer, usage: !chickenbet <tokenamt> <seconds>")]
    [InlineData("chickenbet", "abc", "10", "viewer, please enter a valid token amount.")]
    [InlineData("chickenbet", "10", "abc", "viewer, please enter a valid second value.")]
    [InlineData("chickenbet", "10", "0", "viewer, please enter a valid second value.")]
    [InlineData("chickenbet", "100", "10", "viewer, Chicken Run betting is not open right now.")]
    [InlineData("guess", null, null, "viewer, usage: !guess <number>")]
    [InlineData("guess", "abc", null, "viewer, please enter a valid number between 1 and 100.")]
    [InlineData("guess", "0", null, "viewer, please enter a valid number between 1 and 100.")]
    [InlineData("guess", "101", null, "viewer, please enter a valid number between 1 and 100.")]
    [InlineData("guess", "50", null, "viewer, there is no Guess The Number round active right now.")]
    [InlineData("damagewither", null, null, "viewer, usage: !damagewither <tokenamt> (your token bet is your damage)")]
    [InlineData("damagewither", "abc", null, "viewer, please enter a valid token amount.")]
    [InlineData("damagewither", "201", null, "viewer, the max Wither Battle bet is 200 tokens.")]
    [InlineData("damagewither", "200", null, "viewer, a Wither Battle is not active right now.")]
    public async Task MinigameHandlers_RejectInvalidOrInactiveWithoutCharge(
        string command,
        string? firstArgument,
        string? secondArgument,
        string expectedMessage)
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        MainHandler runtime = new(
            new AppShellViewModel(),
            databasePath);
        Dictionary<string, ChatCommandHandler> handlers = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        List<string> successes = [];
        List<string> arguments = [];
        if (firstArgument != null)
            arguments.Add(firstArgument);
        if (secondArgument != null)
            arguments.Add(secondArgument);

        runtime.Tokens.Award("viewer", 500);
        MinigameManager.AddHandlers(
            runtime,
            handlers,
            (message, _) =>
            {
                errors.Add(message);
                return Task.CompletedTask;
            },
            (message, _) =>
            {
                successes.Add(message);
                return Task.CompletedTask;
            });

        try
        {
            await handlers[command]([.. arguments], "viewer", TestContext.Current.CancellationToken);

            Assert.Equal([expectedMessage], errors);
            Assert.Empty(successes);
            Assert.Equal(500, runtime.Tokens.GetBalance("viewer"));
        }
        finally
        {
            await MinigameManager.StopLoopsAsync(runtime);
            runtime.Tokens.Close();
        }

        TokenStore reopened = new(databasePath);
        try
        {
            Assert.Equal(500, reopened.GetBalance("viewer"));
        }
        finally
        {
            reopened.CloseConnection();
        }
    }
}
