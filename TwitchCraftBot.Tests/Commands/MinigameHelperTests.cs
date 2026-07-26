using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Commands;

public sealed class MinigameHelperTests
{
    [Theory]
    [InlineData(1, "1 token")]
    [InlineData(2, "2 tokens")]
    public void FormatTokens_UsesCorrectSingularAndPluralForms(int amount, string expected)
    {
        Assert.Equal(expected, MinigameManager.FormatTokens(amount));
    }

    [Fact]
    public async Task TrySayBetFailureAsync_ReportsEveryRejectedBetReason()
    {
        List<string> messages = [];
        Func<string, CancellationToken, Task> capture = (message, _) =>
        {
            messages.Add(message);
            return Task.CompletedTask;
        };

        Assert.True(await MinigameManager.TrySayBetFailureAsync(
            MinigameManager.MinigameBetUpdateResult.NotEnoughTokens,
            "viewer",
            "chicken run",
            "betting is closed.",
            capture,
            CancellationToken.None));
        Assert.True(await MinigameManager.TrySayBetFailureAsync(
            MinigameManager.MinigameBetUpdateResult.OverMax,
            "viewer",
            "chicken run",
            "betting is closed.",
            capture,
            CancellationToken.None));
        Assert.True(await MinigameManager.TrySayBetFailureAsync(
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
    public async Task TrySayBetFailureAsync_DoesNotSendForSuccessfulUpdate()
    {
        bool sent = false;

        bool handled = await MinigameManager.TrySayBetFailureAsync(
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
    [InlineData(false, 0, 1, false, 0)]
    [InlineData(true, 0, 0, true, 0)]
    [InlineData(true, 2, 2, true, 25)]
    [InlineData(true, 3, 3, true, 25)]
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
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);
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
            runtime.CloseTokenStoreConnection();
        }
    }
}
