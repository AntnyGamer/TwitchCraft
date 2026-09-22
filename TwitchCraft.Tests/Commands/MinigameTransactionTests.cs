using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft.Tests.Economy;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class MinigameTransactionTests
{
    [Fact]
    public async Task ReplyBetErrorAsync_ReportsEveryRejectedBetReason()
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

        Assert.Equal(
            [
                "viewer, you do not have enough tokens for that bet.",
                "viewer, the max chicken run bet is 200 tokens.",
                "viewer, betting is closed."
            ],
            messages);
    }

    [Fact]
    public async Task ReplyBetErrorAsync_DoesNotSendForSuccessfulUpdate()
    {
        bool sent = false;

        bool handled = await MinigameManager.ReplyBetErrorAsync(
            MinigameManager.BetUpdateResult.Updated,
            "viewer",
            "chicken run",
            "betting is closed.",
            (_, _) =>
            {
                sent = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(handled);
        Assert.False(sent);
    }

    [Fact]
    public async Task RejectedMinigameBet_PreservesPersistedTokenBalance()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        MainHandler runtime = new(new AppShellViewModel(), databasePath);
        Dictionary<string, ChatCommandHandler> handlers = new(StringComparer.OrdinalIgnoreCase);
        MinigameManager.AddHandlers(
            runtime,
            handlers,
            static (_, _) => Task.CompletedTask,
            static (_, _) => Task.CompletedTask);

        try
        {
            runtime.Tokens.Award("viewer", 500);
            await handlers["chickenbet"](["100", "10"], "viewer", TestContext.Current.CancellationToken);
            Assert.Equal(500, runtime.Tokens.GetBalance("viewer"));
        }
        finally
        {
            await MinigameManager.StopLoopsAsync(runtime);
            runtime.Tokens.Close();
        }

        MainHandler reopened = new(new AppShellViewModel(), databasePath);
        try
        {
            Assert.Equal(500, reopened.Tokens.GetBalance("viewer"));
        }
        finally
        {
            reopened.Tokens.Close();
        }
    }

    [Theory]
    [InlineData("chickenbet", null, null, "viewer, usage: !chickenbet <tokenamt> <seconds>")]
    [InlineData("chickenbet", "abc", "10", "viewer, please enter a valid token amount.")]
    [InlineData("chickenbet", "10", "abc", "viewer, please enter a valid second value.")]
    [InlineData("chickenbet", "10", "0", "viewer, please enter a valid second value.")]
    [InlineData("chickenbet", "10", "10", "viewer, Chicken Run betting is not open right now.")]
    [InlineData("guess", null, null, "viewer, usage: !guess <number>")]
    [InlineData("guess", "abc", null, "viewer, please enter a valid number between 1 and 100.")]
    [InlineData("guess", "0", null, "viewer, please enter a valid number between 1 and 100.")]
    [InlineData("guess", "101", null, "viewer, please enter a valid number between 1 and 100.")]
    [InlineData("guess", "50", null, "viewer, there is no Guess The Number round active right now.")]
    [InlineData("damagewither", null, null, "viewer, usage: !damagewither <tokenamt> (your token bet is your damage)")]
    [InlineData("damagewither", "abc", null, "viewer, please enter a valid token amount.")]
    [InlineData("damagewither", "201", null, "viewer, the max Wither Battle bet is 200 tokens.")]
    [InlineData("damagewither", "200", null, "viewer, a Wither Battle is not active right now.")]
    public async Task MinigameHandlers_RejectInvalidOrInactiveRequestsWithoutSpendingTokens(
        string command,
        string? firstArgument,
        string? secondArgument,
        string expectedMessage)
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
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
    }
}
