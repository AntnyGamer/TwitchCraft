using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot.Tests.Revamped.Economy;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class MinigameTransactionBehaviorTests
{
    private const int Updated = (int)MinigameManager.MinigameBetUpdateResult.Updated;
    private const int NotEnoughTokens = (int)MinigameManager.MinigameBetUpdateResult.NotEnoughTokens;
    private const int OverMax = (int)MinigameManager.MinigameBetUpdateResult.OverMax;
    private const int Closed = (int)MinigameManager.MinigameBetUpdateResult.Closed;

    [Fact]
    public async Task ReplyBetErrorAsync_ReportsEveryRejectedBetReason()
    {
        List<string> messages = [];
        Func<string, CancellationToken, Task> capture = (message, _) =>
        {
            messages.Add(message);
            return Task.CompletedTask;
        };

        Assert.True(await MinigameManager.ReplyBetErrorAsync(
            MinigameManager.MinigameBetUpdateResult.NotEnoughTokens,
            "viewer",
            "chicken run",
            "betting is closed.",
            capture,
            CancellationToken.None));
        Assert.True(await MinigameManager.ReplyBetErrorAsync(
            MinigameManager.MinigameBetUpdateResult.OverMax,
            "viewer",
            "chicken run",
            "betting is closed.",
            capture,
            CancellationToken.None));
        Assert.True(await MinigameManager.ReplyBetErrorAsync(
            MinigameManager.MinigameBetUpdateResult.Closed,
            "viewer",
            "chicken run",
            "betting is closed.",
            capture,
            CancellationToken.None));

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
            MinigameManager.MinigameBetUpdateResult.Updated,
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
    public void AddMinigameHandlers_RegistersEveryMinigameCommand()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        Dictionary<string, ChatCommandHandler> handlers = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            MinigameManager.AddMinigameHandlers(
                runtime,
                handlers,
                static (_, _) => Task.CompletedTask,
                static (_, _) => Task.CompletedTask);

            Assert.Equal(["chickenbet", "damagewither", "guess"], handlers.Keys.Order());
        }
        finally
        {
            runtime.Tokens.Close();
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
        BotMainHandler runtime = new(
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

        MinigameManager.AddMinigameHandlers(
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
            Assert.Equal(0, runtime.Tokens.GetBalance("viewer"));
        }
        finally
        {
            await MinigameManager.StopLoopsAsync(runtime);
            runtime.Tokens.Close();
        }
    }

    [Theory]
    [InlineData(false, Updated, NotEnoughTokens, false, 0)]
    [InlineData(true, Updated, Updated, true, 0)]
    [InlineData(true, OverMax, OverMax, true, 25)]
    [InlineData(true, Closed, Closed, true, 25)]
    public void TryAddPaidBet_ChargesUpdatesAndRefundsAtomically(
        bool spendSucceeds,
        int updateResultValue,
        int expectedValue,
        bool updateCalled,
        int expectedRefund)
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        bool updated = false;
        var updateResult = (MinigameManager.MinigameBetUpdateResult)updateResultValue;
        var expected = (MinigameManager.MinigameBetUpdateResult)expectedValue;
        if (spendSucceeds)
            runtime.Tokens.Adjust("viewer", 25);

        try
        {
            MinigameManager.MinigameBetUpdateResult result = MinigameManager.TryAddPaidBet(
                runtime,
                "viewer",
                25,
                () =>
                {
                    updated = true;
                    return updateResult;
                });

            Assert.Equal(expected, result);
            Assert.Equal(updateCalled, updated);
            Assert.Equal(expectedRefund, runtime.Tokens.GetBalance("viewer"));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
