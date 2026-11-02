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
        Func<string, CancellationToken, Task> capture = CaptureMessages(messages);

        foreach (MinigameManager.BetUpdateResult result in new[]
        {
            MinigameManager.BetUpdateResult.NotEnoughTokens,
            MinigameManager.BetUpdateResult.OverMax,
            MinigameManager.BetUpdateResult.Closed
        })
        {
            Assert.True(await MinigameManager.ReplyBetErrorAsync(
                result, "viewer", "chicken run", "betting is closed.", capture, CancellationToken.None), result.ToString());
        }

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
        MethodInfo RequireMethod(string name) => Assert.IsAssignableFrom<MethodInfo>(manager.GetMethod(name, flags));
        MethodInfo start = RequireMethod("TryStart");
        MethodInfo getState = RequireMethod("GetChickenStateNoLock");
        MethodInfo settle = RequireMethod("SettleBets");
        MethodInfo refund = RequireMethod("RefundBets");

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

    [Fact]
    public async Task MinigameHandlers_RejectInvalidOrInactiveCasesWithoutCharge()
    {
        (string Command, string? First, string? Second, string Expected)[] cases =
        [
            ("chickenbet", null, null, "viewer, usage: !chickenbet <tokenamt> <seconds>"),
            ("chickenbet", "abc", "10", "viewer, please enter a valid token amount."),
            ("chickenbet", "10", "abc", "viewer, please enter a valid second value."),
            ("chickenbet", "10", "0", "viewer, please enter a valid second value."),
            ("chickenbet", "100", "10", "viewer, Chicken Run betting is not open right now."),
            ("chickenbet", "201", "10", "viewer, the max Chicken Run bet is 200 tokens."),
            ("guess", null, null, "viewer, usage: !guess <number>"),
            ("guess", "abc", null, "viewer, please enter a valid number between 1 and 100."),
            ("guess", "0", null, "viewer, please enter a valid number between 1 and 100."),
            ("guess", "101", null, "viewer, please enter a valid number between 1 and 100."),
            ("guess", "50", null, "viewer, there is no Guess The Number round active right now."),
            ("damagewither", null, null, "viewer, usage: !damagewither <tokenamt> (your token bet is your damage)"),
            ("damagewither", "abc", null, "viewer, please enter a valid token amount."),
            ("damagewither", "201", null, "viewer, the max Wither Battle bet is 200 tokens."),
            ("damagewither", "200", null, "viewer, a Wither Battle is not active right now.")
        ];
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        MainHandler runtime = new(new AppShellViewModel(), databasePath);
        Dictionary<string, ChatCommandHandler> handlers = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        List<string> successes = [];
        List<(string Expected, string[] Errors, string[] Successes, int Balance, long Persisted, long Pending)> results = [];

        runtime.Tokens.Award("viewer", 500);
        MinigameManager.AddHandlers(runtime, handlers, CaptureMessages(errors), CaptureMessages(successes));

        try
        {
            using SqliteConnection observer = new($"Data Source={databasePath}");
            observer.Open();
            using SqliteCommand balanceQuery = observer.CreateCommand();
            balanceQuery.CommandText = "SELECT Balance FROM TokenBalances WHERE Username = 'viewer';";
            using SqliteCommand pendingQuery = observer.CreateCommand();
            pendingQuery.CommandText = "SELECT COUNT(*) FROM PendingMinigameBets;";

            foreach ((string command, string? first, string? second, string expected) in cases)
            {
                errors.Clear();
                successes.Clear();
                List<string> arguments = [];
                if (first != null)
                {
                    arguments.Add(first);
                }

                if (second != null)
                {
                    arguments.Add(second);
                }

                await handlers[command]([.. arguments], "viewer", TestContext.Current.CancellationToken);

                results.Add((
                    expected,
                    [.. errors],
                    [.. successes],
                    runtime.Tokens.GetBalance("viewer"),
                    Convert.ToInt64(balanceQuery.ExecuteScalar()),
                    Convert.ToInt64(pendingQuery.ExecuteScalar())));
                await MinigameManager.StopLoopsAsync(runtime);
            }

            Assert.All(results, result => Assert.Multiple(
                () => Assert.Equal([result.Expected], result.Errors),
                () => Assert.Empty(result.Successes),
                () => Assert.Equal(500, result.Balance),
                () => Assert.Equal(500L, result.Persisted),
                () => Assert.Equal(0L, result.Pending)));
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

    private static Func<string, CancellationToken, Task> CaptureMessages(List<string> messages)
        => (message, _) =>
        {
            messages.Add(message);
            return Task.CompletedTask;
        };
}
