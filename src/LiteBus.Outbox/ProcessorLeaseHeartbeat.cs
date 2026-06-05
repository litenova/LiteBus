using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Renews outbox leases on an interval while publication work is still running.
/// </summary>
internal static class ProcessorLeaseHeartbeat
{
    /// <summary>
    ///     Runs an outbox operation and renews the active lease on a background loop until the operation completes.
    /// </summary>
    /// <typeparam name="T">The result type returned by the operation.</typeparam>
    /// <param name="messageId">The identifier of the leased message.</param>
    /// <param name="leaseOwner">The publisher name that currently owns the lease.</param>
    /// <param name="leaseStore">The lease store used to extend ownership.</param>
    /// <param name="leaseDuration">The duration applied on each renewal from the current UTC time.</param>
    /// <param name="heartbeatInterval">The delay between renewal attempts.</param>
    /// <param name="clock">The time provider used to compute renewal expirations.</param>
    /// <param name="operation">The publication work executed while renewals continue.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>The value returned by <paramref name="operation" />.</returns>
    public static async Task<T> RunWithHeartbeatAsync<T>(
        Guid messageId,
        string leaseOwner,
        IOutboxLeaseStore leaseStore,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        TimeProvider clock,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(operation);

        if (heartbeatInterval <= TimeSpan.Zero)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RenewLoopAsync(
            messageId,
            leaseOwner,
            leaseStore,
            leaseDuration,
            heartbeatInterval,
            clock,
            heartbeatCts.Token);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>
    ///     Extends the lease on a fixed interval until cancellation is requested.
    /// </summary>
    /// <param name="messageId">The identifier of the leased message.</param>
    /// <param name="leaseOwner">The publisher name that currently owns the lease.</param>
    /// <param name="leaseStore">The lease store used to extend ownership.</param>
    /// <param name="leaseDuration">The duration applied on each renewal from the current UTC time.</param>
    /// <param name="heartbeatInterval">The delay between renewal attempts.</param>
    /// <param name="clock">The time provider used to compute renewal expirations.</param>
    /// <param name="cancellationToken">A token that stops the renewal loop.</param>
    /// <returns>A task that completes when the loop is canceled.</returns>
    private static async Task RenewLoopAsync(
        Guid messageId,
        string leaseOwner,
        IOutboxLeaseStore leaseStore,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(heartbeatInterval, cancellationToken).ConfigureAwait(false);

            var expiresAt = clock.GetUtcNow().Add(leaseDuration);
            await leaseStore.RenewLeaseAsync(messageId, leaseOwner, expiresAt, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
