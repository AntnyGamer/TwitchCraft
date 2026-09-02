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
            runtime.AdjustTokens("viewer", 25);

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
            Assert.Equal(expectedRefund, runtime.GetTokens("viewer"));
        }
        finally
        {
            runtime.CloseTokenStore();
        }
    }
}
