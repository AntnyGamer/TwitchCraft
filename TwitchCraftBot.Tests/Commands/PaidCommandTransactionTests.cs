using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Commands;

public sealed class PaidCommandTransactionTests
{
    [Fact]
    public async Task SuccessfulPaidCommand_ChargesOnceDispatchesOnceAndRecordsStatisticsOnce()
    {
        TransactionHarness harness = new();

        bool succeeded = await harness.ExecuteAsync(25);

        Assert.True(succeeded);
        Assert.Equal(75, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(0, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(1, harness.StatisticsCalls);
        Assert.Equal(0, harness.FailureNotifications);
        Assert.Empty(harness.ReleasedReservations);
        Assert.Equal(101, harness.CurrentReservation);
    }

    [Fact]
    public async Task FailedPaidCommandDispatch_RefundsExactlyOnceReportsFailureAndRecordsNoStatistics()
    {
        TransactionHarness harness = new() { DispatchResult = false };

        bool succeeded = await harness.ExecuteAsync(25);

        Assert.False(succeeded);
        Assert.Equal(100, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(1, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal(1, harness.DispatchFailureReports);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal(0, harness.CurrentReservation);
        Assert.Equal([101L], harness.ReleasedReservations);
    }

    [Fact]
    public async Task ExceptionWhileBuildingPaidCommandBatch_RefundsOnceAndReleasesCooldown()
    {
        TransactionHarness harness = new()
        {
            DispatchOverride = _ => throw new InvalidOperationException("Command batch could not be built.")
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ExecuteAsync(25));

        Assert.Contains("could not be built", exception.Message);
        Assert.Equal(100, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(1, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal(0, harness.CurrentReservation);
        Assert.Equal([101L], harness.ReleasedReservations);
    }

    [Fact]
    public async Task FailedDispatch_ReleasesOnlyItsOwnCooldownReservation()
    {
        TransactionHarness harness = new();
        harness.DispatchOverride = _ =>
        {
            harness.CurrentReservation = 202;
            return Task.FromResult(false);
        };

        bool succeeded = await harness.ExecuteAsync(10);

        Assert.False(succeeded);
        Assert.Equal(100, harness.Balance);
        Assert.Equal(1, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal([101L], harness.ReleasedReservations);
        Assert.Equal(202, harness.CurrentReservation);
    }

    [Fact]
    public async Task InsufficientBalance_DoesNotDispatchAndReleasesCooldown()
    {
        TransactionHarness harness = new();

        bool succeeded = await harness.ExecuteAsync(125);

        Assert.False(succeeded);
        Assert.Equal(100, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(0, harness.DispatchCalls);
        Assert.Equal(0, harness.RefundCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal(1, harness.InsufficientTokenReports);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal([101L], harness.ReleasedReservations);
        Assert.Equal(0, harness.CurrentReservation);
    }

    [Fact]
    public async Task MissingCooldownReservation_DoesNotChargeOrDispatch()
    {
        TransactionHarness harness = new() { NextReservation = null };

        bool succeeded = await harness.ExecuteAsync(25);

        Assert.False(succeeded);
        Assert.Equal(100, harness.Balance);
        Assert.Equal(0, harness.SpendCalls);
        Assert.Equal(0, harness.RefundCalls);
        Assert.Equal(0, harness.DispatchCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal(0, harness.FailureNotifications);
        Assert.Empty(harness.ReleasedReservations);
    }

    [Fact]
    public async Task MultiCommandBatch_RecordsOnlyAfterFullSuccessAndRefundsOnceOnFailure()
    {
        string[] commands = ["first command", "second command", "third command"];
        TransactionHarness successful = new();
        successful.DispatchOverride = _ =>
        {
            Assert.Equal(0, successful.StatisticsCalls);
            successful.DispatchedCommands.AddRange(commands);
            return Task.FromResult(true);
        };

        bool successResult = await successful.ExecuteAsync(30);

        Assert.True(successResult);
        Assert.Equal(commands, successful.DispatchedCommands);
        Assert.Equal(70, successful.Balance);
        Assert.Equal(1, successful.DispatchCalls);
        Assert.Equal(0, successful.RefundCalls);
        Assert.Equal(1, successful.StatisticsCalls);
        Assert.Empty(successful.ReleasedReservations);
        Assert.Equal(101, successful.CurrentReservation);

        TransactionHarness failed = new() { DispatchResult = false };
        failed.DispatchOverride = _ =>
        {
            Assert.Equal(0, failed.StatisticsCalls);
            failed.DispatchedCommands.AddRange(commands);
            return Task.FromResult(false);
        };

        bool failureResult = await failed.ExecuteAsync(30);

        Assert.False(failureResult);
        Assert.Equal(commands, failed.DispatchedCommands);
        Assert.Equal(100, failed.Balance);
        Assert.Equal(1, failed.DispatchCalls);
        Assert.Equal(1, failed.RefundCalls);
        Assert.Equal(0, failed.StatisticsCalls);
        Assert.Equal([101L], failed.ReleasedReservations);
        Assert.Equal(0, failed.CurrentReservation);
    }

    private sealed class TransactionHarness
    {
        internal int Balance { get; private set; } = 100;
        internal int SpendCalls { get; private set; }
        internal int RefundCalls { get; private set; }
        internal int DispatchCalls { get; private set; }
        internal int StatisticsCalls { get; private set; }
        internal int DispatchFailureReports { get; private set; }
        internal int InsufficientTokenReports { get; private set; }
        internal int FailureNotifications { get; private set; }
        internal long CurrentReservation { get; set; }
        internal long? NextReservation { get; set; } = 101;
        internal bool DispatchResult { get; set; } = true;
        internal Func<CancellationToken, Task<bool>>? DispatchOverride { get; set; }
        internal List<long> ReleasedReservations { get; } = [];
        internal List<string> DispatchedCommands { get; } = [];

        internal Task<bool> ExecuteAsync(int cost)
        {
            PaidCommandTransactionDependencies dependencies = new()
            {
                ReserveCooldownAsync = _ =>
                {
                    if (NextReservation.HasValue)
                        CurrentReservation = NextReservation.Value;

                    return Task.FromResult(NextReservation);
                },
                ReleaseCooldown = reservation =>
                {
                    ReleasedReservations.Add(reservation);
                    if (CurrentReservation == reservation)
                        CurrentReservation = 0;
                },
                TrySpendTokens = amount =>
                {
                    SpendCalls++;
                    if (Balance < amount)
                        return false;

                    Balance -= amount;
                    return true;
                },
                RefundTokens = amount =>
                {
                    RefundCalls++;
                    Balance += amount;
                },
                DispatchAsync = token =>
                {
                    DispatchCalls++;
                    return DispatchOverride?.Invoke(token) ?? Task.FromResult(DispatchResult);
                },
                RecordStatistics = _ => StatisticsCalls++,
                ReportInsufficientTokensAsync = (_, _) =>
                {
                    InsufficientTokenReports++;
                    return Task.CompletedTask;
                },
                ReportDispatchFailureAsync = _ =>
                {
                    DispatchFailureReports++;
                    return Task.CompletedTask;
                },
                NotifyFailure = () => FailureNotifications++
            };

            return PaidCommandTransaction.ExecuteAsync(dependencies, cost, CancellationToken.None);
        }
    }
}
