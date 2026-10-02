using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Commands;

public sealed class PaidDispatchAtomicityTests
{
    [Fact]
    public async Task SuccessfulPaidCommand_ChargesDispatchesAndRecordsOnce()
    {
        TransactionHarness harness = new();
        harness.DispatchOverride = _ =>
        {
            Assert.Equal(0, harness.StatisticsCalls);
            return Task.FromResult(true);
        };

        bool succeeded = await harness.ExecuteAsync(25, TestContext.Current.CancellationToken);

        Assert.True(succeeded);
        Assert.Equal(75, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(0, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(1, harness.StatisticsCalls);
        Assert.Equal(25, harness.RecordedCost);
        Assert.Equal(0, harness.FailureNotifications);
        Assert.Empty(harness.ReleasedReservations);
        Assert.Equal(101, harness.CurrentReservation);
    }

    [Fact]
    public async Task FailedPaidCommand_RefundsOnceReportsFailureAndSkipsStatistics()
    {
        TransactionHarness harness = new();
        harness.DispatchOverride = _ =>
        {
            Assert.Equal(0, harness.StatisticsCalls);
            return Task.FromResult(false);
        };

        bool succeeded = await harness.ExecuteAsync(25, TestContext.Current.CancellationToken);

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
    public async Task PaidCommandBuildFailure_RefundsOnceAndReleasesCooldown()
    {
        TransactionHarness harness = new()
        {
            DispatchOverride = _ => throw new InvalidOperationException("Command batch could not be built.")
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ExecuteAsync(25, TestContext.Current.CancellationToken));

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

        bool succeeded = await harness.ExecuteAsync(10, TestContext.Current.CancellationToken);

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

        bool succeeded = await harness.ExecuteAsync(125, TestContext.Current.CancellationToken);

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

        bool succeeded = await harness.ExecuteAsync(25, TestContext.Current.CancellationToken);

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
    public async Task CancellationDuringDispatch_RefundsAndReleasesCooldown()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        TaskCompletionSource dispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> dispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TransactionHarness harness = new()
        {
            DispatchOverride = token =>
            {
                dispatchStarted.SetResult();
                return dispatch.Task.WaitAsync(token);
            }
        };

        Task<bool> execution = harness.ExecuteAsync(25, cancellation.Token);
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(75, harness.Balance);
        Assert.Equal(1, harness.DispatchCalls);

        cancellation.Cancel();
        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(100, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(1, harness.RefundCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal(0, harness.DispatchFailureReports);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal([101L], harness.ReleasedReservations);
        Assert.Equal(0, harness.CurrentReservation);
    }

    [Fact]
    public async Task FailedRefund_IsReportedAndStillReleasesCooldown()
    {
        TransactionHarness harness = new()
        {
            DispatchOverride = _ => Task.FromResult(false),
            RefundOverride = _ => false
        };

        Assert.False(await harness.ExecuteAsync(25, TestContext.Current.CancellationToken));

        Assert.Equal(75, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(1, harness.RefundCalls);
        Assert.Equal(false, harness.ReportedRefundSucceeded);
        Assert.Equal(1, harness.DispatchFailureReports);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal([101L], harness.ReleasedReservations);
        Assert.Equal(0, harness.CurrentReservation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ZeroCostCommand_DispatchesWithoutSpendingOrRefunding(bool dispatchSucceeded)
    {
        TransactionHarness harness = new()
        {
            DispatchOverride = _ => Task.FromResult(dispatchSucceeded)
        };

        Assert.Equal(dispatchSucceeded, await harness.ExecuteAsync(0, TestContext.Current.CancellationToken));

        Assert.Equal(100, harness.Balance);
        Assert.Equal(0, harness.SpendCalls);
        Assert.Equal(0, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(dispatchSucceeded ? 1 : 0, harness.StatisticsCalls);
        Assert.Equal(dispatchSucceeded ? 0 : 1, harness.DispatchFailureReports);
        Assert.Equal(dispatchSucceeded ? 0 : 1, harness.FailureNotifications);
        if (dispatchSucceeded)
        {
            Assert.Equal(0, harness.RecordedCost);
            Assert.Empty(harness.ReleasedReservations);
        }
        else
        {
            Assert.Null(harness.RecordedCost);
            Assert.Equal(true, harness.ReportedRefundSucceeded);
            Assert.Equal([101L], harness.ReleasedReservations);
        }
        Assert.Equal(dispatchSucceeded ? 101 : 0, harness.CurrentReservation);
    }

    private sealed class TransactionHarness
    {
        internal int Balance { get; private set; } = 100;
        internal int SpendCalls { get; private set; }
        internal int RefundCalls { get; private set; }
        internal int DispatchCalls { get; private set; }
        internal int StatisticsCalls { get; private set; }
        internal int? RecordedCost { get; private set; }
        internal int DispatchFailureReports { get; private set; }
        internal int InsufficientTokenReports { get; private set; }
        internal int FailureNotifications { get; private set; }
        internal long CurrentReservation { get; set; }
        internal long? NextReservation { get; set; } = 101;
        internal Func<CancellationToken, Task<bool>>? DispatchOverride { get; set; }
        internal Func<int, bool>? RefundOverride { get; set; }
        internal bool? ReportedRefundSucceeded { get; private set; }
        internal List<long> ReleasedReservations { get; } = [];
        internal Task<bool> ExecuteAsync(int cost, CancellationToken cancellationToken)
        {
            return PaidCommandTransaction.ExecuteAsync(
                cost,
                _ =>
                {
                    if (NextReservation.HasValue)
                        CurrentReservation = NextReservation.Value;

                    return Task.FromResult(NextReservation);
                },
                reservation =>
                {
                    ReleasedReservations.Add(reservation);
                    if (CurrentReservation == reservation)
                        CurrentReservation = 0;
                },
                amount =>
                {
                    SpendCalls++;
                    if (Balance < amount)
                        return false;

                    Balance -= amount;
                    return true;
                },
                amount =>
                {
                    RefundCalls++;
                    if (RefundOverride != null)
                        return RefundOverride(amount);
                    Balance += amount;
                    return true;
                },
                token =>
                {
                    DispatchCalls++;
                    return DispatchOverride?.Invoke(token) ?? Task.FromResult(true);
                },
                amount =>
                {
                    StatisticsCalls++;
                    RecordedCost = amount;
                },
                (_, _) =>
                {
                    InsufficientTokenReports++;
                    return Task.CompletedTask;
                },
                (refunded, _) =>
                {
                    DispatchFailureReports++;
                    ReportedRefundSucceeded = refunded;
                    return Task.CompletedTask;
                },
                cancellationToken,
                () => FailureNotifications++);
        }
    }
}
