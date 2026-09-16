using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal sealed class PaidCommandDependencies
{
    internal required Func<CancellationToken, Task<long?>> ReserveCooldownAsync { get; init; }
    internal required Action<long> ReleaseCooldown { get; init; }
    internal required Func<int, bool> TrySpendTokens { get; init; }
    internal required Func<int, bool> RefundTokens { get; init; }
    internal required Func<CancellationToken, Task<bool>> DispatchAsync { get; init; }
    internal required Action<int> RecordStatistics { get; init; }
    internal required Func<int, CancellationToken, Task> ReportInsufficientTokensAsync { get; init; }
    internal required Func<bool, CancellationToken, Task> ReportDispatchFailureAsync { get; init; }
    internal Action? NotifyFailure { get; init; }
}

internal static class PaidCommandTransaction
{
    internal static async Task<bool> ExecuteAsync(
        PaidCommandDependencies dependencies,
        int cost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentOutOfRangeException.ThrowIfNegative(cost);

        long? cooldownReservation = await dependencies.ReserveCooldownAsync(cancellationToken).ConfigureAwait(false);
        if (!cooldownReservation.HasValue)
            return false;

        bool charged = false;
        bool refundAttempted = false;
        bool refundSucceeded = false;
        bool failureNotified = false;
        bool dispatchSucceeded = false;

        bool RefundOnce()
        {
            if (!charged || cost <= 0) return true;
            if (refundAttempted) return refundSucceeded;

            refundAttempted = true;
            refundSucceeded = dependencies.RefundTokens(cost);
            if (!refundSucceeded)
                ErrorHandling.LogNonFatal("A paid command could not fully refund its token charge", new InvalidOperationException("The token refund amount did not match the original charge."));
            return refundSucceeded;
        }

        void NotifyFailureOnce()
        {
            if (failureNotified)
                return;

            failureNotified = true;
            dependencies.NotifyFailure?.Invoke();
        }

        try
        {
            if (cost > 0)
            {
                if (!dependencies.TrySpendTokens(cost))
                {
                    NotifyFailureOnce();
                    await dependencies.ReportInsufficientTokensAsync(cost, cancellationToken).ConfigureAwait(false);
                    return false;
                }

                charged = true;
            }

            try
            {
                dispatchSucceeded = await dependencies.DispatchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RefundOnce();
                NotifyFailureOnce();
                throw;
            }

            if (!dispatchSucceeded)
            {
                bool refunded = RefundOnce();
                NotifyFailureOnce();
                await dependencies.ReportDispatchFailureAsync(refunded, cancellationToken).ConfigureAwait(false);
                return false;
            }

            dependencies.RecordStatistics(cost);
            return true;
        }
        finally
        {
            if (!dispatchSucceeded)
                dependencies.ReleaseCooldown(cooldownReservation.Value);
        }
    }
}
