using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

internal sealed class PaidCommandTransactionDependencies
{
    internal required Func<CancellationToken, Task<long?>> ReserveCooldownAsync { get; init; }
    internal required Action<long> ReleaseCooldown { get; init; }
    internal required Func<int, bool> TrySpendTokens { get; init; }
    internal required Action<int> RefundTokens { get; init; }
    internal required Func<CancellationToken, Task<bool>> DispatchAsync { get; init; }
    internal required Action<int> RecordStatistics { get; init; }
    internal required Func<int, CancellationToken, Task> ReportInsufficientTokensAsync { get; init; }
    internal required Func<CancellationToken, Task> ReportDispatchFailureAsync { get; init; }
    internal Action? NotifyFailure { get; init; }
}

internal static class PaidCommandTransaction
{
    internal static async Task<bool> ExecuteAsync(
        PaidCommandTransactionDependencies dependencies,
        int cost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentOutOfRangeException.ThrowIfNegative(cost);

        long? cooldownReservation = await dependencies.ReserveCooldownAsync(cancellationToken).ConfigureAwait(false);
        if (!cooldownReservation.HasValue)
            return false;

        bool charged = false;
        bool refunded = false;
        bool failureNotified = false;
        bool dispatchSucceeded = false;

        void RefundOnce()
        {
            if (!charged || refunded || cost <= 0)
                return;

            dependencies.RefundTokens(cost);
            refunded = true;
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

            bool sent;
            try
            {
                sent = await dependencies.DispatchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RefundOnce();
                NotifyFailureOnce();
                throw;
            }

            if (!sent)
            {
                RefundOnce();
                NotifyFailureOnce();
                await dependencies.ReportDispatchFailureAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            dispatchSucceeded = true;
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
