using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Commands;

public sealed class PaidDispatchAtomicityTests
{
    [Fact]
    public async Task SuccessfulPaidCommand_ChargesOnceDispatchesOnceAndRecordsStatisticsOnce()
    {
        TransactionHarness harness = new();
        harness.DispatchOverride = _ =>
        {
            Assert.Equal(0, harness.StatisticsCalls);
            return Task.FromResult(true);
        };

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
        TransactionHarness harness = new();
        harness.DispatchOverride = _ =>
        {
            Assert.Equal(0, harness.StatisticsCalls);
            return Task.FromResult(false);
        };

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

        Assert.Contains("could not be built", exception.Message, StringComparison.Ordinal);
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
        internal Func<CancellationToken, Task<bool>>? DispatchOverride { get; set; }
        internal List<long> ReleasedReservations { get; } = [];
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
                    return DispatchOverride?.Invoke(token) ?? Task.FromResult(true);
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
