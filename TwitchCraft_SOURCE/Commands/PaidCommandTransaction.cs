using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal static class PaidCommandTransaction
{
    internal static async Task<bool> ExecuteAsync(
        int cost,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<long?>> reserveCooldownAsync,
        Action<long> releaseCooldown,
        Func<int, bool> trySpendTokens,
        Func<int, bool> refundTokens,
        Func<CancellationToken, Task<bool>> dispatchAsync,
        Action<int> recordStatistics,
        Func<int, CancellationToken, Task> reportInsufficientTokensAsync,
        Func<bool, CancellationToken, Task> reportDispatchFailureAsync,
        Action? notifyFailure = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cost);

        long? cooldownReservation = await reserveCooldownAsync(cancellationToken).ConfigureAwait(false);
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
            refundSucceeded = refundTokens(cost);
            if (!refundSucceeded)
                ErrorHandling.LogNonFatal("A paid command could not fully refund its token charge", new InvalidOperationException("The token refund amount did not match the original charge."));
            return refundSucceeded;
        }

        void NotifyFailureOnce()
        {
            if (failureNotified)
                return;

            failureNotified = true;
            notifyFailure?.Invoke();
        }

        try
        {
            if (cost > 0)
            {
                if (!trySpendTokens(cost))
                {
                    NotifyFailureOnce();
                    await reportInsufficientTokensAsync(cost, cancellationToken).ConfigureAwait(false);
                    return false;
                }

                charged = true;
            }

            try
            {
                dispatchSucceeded = await dispatchAsync(cancellationToken).ConfigureAwait(false);
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
                await reportDispatchFailureAsync(refunded, cancellationToken).ConfigureAwait(false);
                return false;
            }

            recordStatistics(cost);
            return true;
        }
        finally
        {
            if (!dispatchSucceeded)
                releaseCooldown(cooldownReservation.Value);
        }
    }
}
