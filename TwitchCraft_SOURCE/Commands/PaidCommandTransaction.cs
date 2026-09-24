using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal static class PaidCommandTransaction
{
    internal static async Task<bool> ExecuteAsync(
        int cost,
        Func<CancellationToken, Task<long?>> reserveCooldownAsync,
        Action<long> releaseCooldown,
        Func<int, bool> trySpendTokens,
        Func<int, bool> refundTokens,
        Func<CancellationToken, Task<bool>> dispatchAsync,
        Action<int> recordStatistics,
        Func<int, CancellationToken, Task> reportInsufficientTokensAsync,
        Func<bool, CancellationToken, Task> reportDispatchFailureAsync,
        Action? notifyFailure,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cost);

        long? cooldownReservation = await reserveCooldownAsync(cancellationToken).ConfigureAwait(false);
        if (!cooldownReservation.HasValue)
            return false;

        bool dispatchSucceeded = false;

        bool Refund()
        {
            if (cost <= 0) return true;
            bool refunded = refundTokens(cost);
            if (!refunded)
                ErrorHandling.LogNonFatal("A paid command could not fully refund its token charge", new InvalidOperationException("The token refund amount did not match the original charge."));
            return refunded;
        }

        try
        {
            if (cost > 0 && !trySpendTokens(cost))
            {
                notifyFailure?.Invoke();
                await reportInsufficientTokensAsync(cost, cancellationToken).ConfigureAwait(false);
                return false;
            }

            try
            {
                dispatchSucceeded = await dispatchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Refund();
                notifyFailure?.Invoke();
                throw;
            }

            if (!dispatchSucceeded)
            {
                bool refunded = Refund();
                notifyFailure?.Invoke();
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
