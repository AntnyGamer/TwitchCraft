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
        Assert.Equal([25], harness.RecordedCosts);
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
        Assert.Equal(true, harness.LastReportedRefund);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal(0, harness.CurrentReservation);
        Assert.Equal([101L], harness.ReleasedReservations);
    }

    [Fact]
    public async Task FailedRefund_ReportsUnrecoveredChargeAndReleasesCooldown()
    {
        TransactionHarness harness = new()
        {
            RefundSucceeds = false,
            DispatchOverride = _ => Task.FromResult(false)
        };

        bool succeeded = await harness.ExecuteAsync(25, TestContext.Current.CancellationToken);

        Assert.False(succeeded);
        Assert.Equal(75, harness.Balance);
        Assert.Equal(1, harness.SpendCalls);
        Assert.Equal(1, harness.RefundCalls);
        Assert.Equal(1, harness.DispatchCalls);
        Assert.Equal(0, harness.StatisticsCalls);
        Assert.Equal(1, harness.DispatchFailureReports);
        Assert.Equal(false, harness.LastReportedRefund);
        Assert.Equal(1, harness.FailureNotifications);
        Assert.Equal([101L], harness.ReleasedReservations);
        Assert.Equal(0, harness.CurrentReservation);
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
        try
        {
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
            Assert.Empty(harness.RecordedCosts);
            Assert.Equal(0, harness.DispatchFailureReports);
            Assert.Equal(0, harness.InsufficientTokenReports);
            Assert.Equal(1, harness.FailureNotifications);
            Assert.Equal([101L], harness.ReleasedReservations);
            Assert.Equal(0, harness.CurrentReservation);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    private sealed class TransactionHarness
    {
        internal int Balance { get; private set; } = 100;
        internal int SpendCalls { get; private set; }
        internal int RefundCalls { get; private set; }
        internal bool RefundSucceeds { get; set; } = true;
        internal int DispatchCalls { get; private set; }
        internal int StatisticsCalls { get; private set; }
        internal List<int> RecordedCosts { get; } = [];
        internal int DispatchFailureReports { get; private set; }
        internal bool? LastReportedRefund { get; private set; }
        internal int InsufficientTokenReports { get; private set; }
        internal int FailureNotifications { get; private set; }
        internal long CurrentReservation { get; set; }
        internal long? NextReservation { get; set; } = 101;
        internal Func<CancellationToken, Task<bool>>? DispatchOverride { get; set; }
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
                    if (RefundSucceeds)
                        Balance += amount;
                    return RefundSucceeds;
                },
                token =>
                {
                    DispatchCalls++;
                    return DispatchOverride?.Invoke(token) ?? Task.FromResult(true);
                },
                amount =>
                {
                    StatisticsCalls++;
                    RecordedCosts.Add(amount);
                },
                (_, _) =>
                {
                    InsufficientTokenReports++;
                    return Task.CompletedTask;
                },
                (refunded, _) =>
                {
                    DispatchFailureReports++;
                    LastReportedRefund = refunded;
                    return Task.CompletedTask;
                },
                cancellationToken,
                () => FailureNotifications++);
        }
    }
}
