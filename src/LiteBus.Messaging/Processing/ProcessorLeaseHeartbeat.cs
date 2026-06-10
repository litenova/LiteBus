using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Renews message leases on an interval while processor dispatch work is still running.
/// </summary>
internal static class ProcessorLeaseHeartbeat
{
    /// <summary>
    ///     Runs processor work and renews the active lease on a background loop until the operation completes.
    /// </summary>
    /// <typeparam name="T">The result type returned by the operation.</typeparam>
    /// <param name="messageId">The identifier of the leased message.</param>
    /// <param name="leaseOwner">The worker name that currently owns the lease.</param>
    /// <param name="leaseStore">The lease store used to extend ownership.</param>
    /// <param name="leaseDuration">The duration applied on each renewal from the current UTC time.</param>
    /// <param name="heartbeatInterval">The delay between renewal attempts after the initial renewal.</param>
    /// <param name="clock">The time provider used to compute renewal expirations.</param>
    /// <param name="operation">The dispatch work executed while renewals continue.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <param name="leaseRenewalFailedMessage">The warning log template used when renewal fails.</param>
    /// <param name="onLeaseLost">An optional callback invoked when lease renewal fails.</param>
    /// <param name="logger">The optional logger used for lease-lost diagnostics.</param>
    /// <returns>The value returned by <paramref name="operation" />.</returns>
    public static async Task<T> RunWithHeartbeatAsync<T>(
        Guid messageId,
        string leaseOwner,
        ILeaseRenewable leaseStore,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        TimeProvider clock,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        string leaseRenewalFailedMessage,
        Action? onLeaseLost = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(leaseRenewalFailedMessage);

        if (heartbeatInterval <= TimeSpan.Zero)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (!await TryRenewLeaseAsync(
                messageId,
                leaseOwner,
                leaseStore,
                leaseDuration,
                clock,
                leaseRenewalFailedMessage,
                onLeaseLost,
                logger,
                operationCts).ConfigureAwait(false))
        {
            throw new OperationCanceledException(operationCts.Token);
        }

        var heartbeatTask = RenewLoopAsync(
            messageId,
            leaseOwner,
            leaseStore,
            leaseDuration,
            heartbeatInterval,
            clock,
            leaseRenewalFailedMessage,
            operationCts,
            onLeaseLost,
            logger,
            cancellationToken);

        try
        {
            return await operation(operationCts.Token).ConfigureAwait(false);
        }
        finally
        {
            await operationCts.CancelAsync().ConfigureAwait(false);

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
    ///     Extends the lease once and cancels dispatch when renewal fails.
    /// </summary>
    /// <param name="messageId">The identifier of the leased message.</param>
    /// <param name="leaseOwner">The worker name that currently owns the lease.</param>
    /// <param name="leaseStore">The lease store used to extend ownership.</param>
    /// <param name="leaseDuration">The duration applied on each renewal from the current UTC time.</param>
    /// <param name="clock">The time provider used to compute renewal expirations.</param>
    /// <param name="leaseRenewalFailedMessage">The warning log template used when renewal fails.</param>
    /// <param name="onLeaseLost">An optional callback invoked when lease renewal fails.</param>
    /// <param name="logger">The optional logger used for lease-lost diagnostics.</param>
    /// <param name="operationCts">The linked token source used to cancel dispatch when the lease is lost.</param>
    /// <returns><see langword="true" /> when the lease was renewed; otherwise <see langword="false" />.</returns>
    private static async Task<bool> TryRenewLeaseAsync(
        Guid messageId,
        string leaseOwner,
        ILeaseRenewable leaseStore,
        TimeSpan leaseDuration,
        TimeProvider clock,
        string leaseRenewalFailedMessage,
        Action? onLeaseLost,
        ILogger? logger,
        CancellationTokenSource operationCts)
    {
        var expiresAt = clock.GetUtcNow().Add(leaseDuration);
        var renewed = await leaseStore.RenewLeaseAsync(messageId, leaseOwner, expiresAt, CancellationToken.None)
            .ConfigureAwait(false);

        if (renewed)
        {
            return true;
        }

        logger?.LogWarning(
            leaseRenewalFailedMessage,
            messageId,
            leaseOwner);

        onLeaseLost?.Invoke();
        await operationCts.CancelAsync().ConfigureAwait(false);
        return false;
    }

    /// <summary>
    ///     Extends the lease on a fixed interval until cancellation is requested.
    /// </summary>
    /// <param name="messageId">The identifier of the leased message.</param>
    /// <param name="leaseOwner">The worker name that currently owns the lease.</param>
    /// <param name="leaseStore">The lease store used to extend ownership.</param>
    /// <param name="leaseDuration">The duration applied on each renewal from the current UTC time.</param>
    /// <param name="heartbeatInterval">The delay between renewal attempts.</param>
    /// <param name="clock">The time provider used to compute renewal expirations.</param>
    /// <param name="leaseRenewalFailedMessage">The warning log template used when renewal fails.</param>
    /// <param name="operationCts">The linked token source used to cancel dispatch when the lease is lost.</param>
    /// <param name="onLeaseLost">An optional callback invoked when lease renewal fails.</param>
    /// <param name="logger">The optional logger used for lease-lost diagnostics.</param>
    /// <param name="cancellationToken">A token that stops the renewal loop.</param>
    /// <returns>A task that completes when the loop is canceled.</returns>
    private static async Task RenewLoopAsync(
        Guid messageId,
        string leaseOwner,
        ILeaseRenewable leaseStore,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        TimeProvider clock,
        string leaseRenewalFailedMessage,
        CancellationTokenSource operationCts,
        Action? onLeaseLost,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !operationCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(heartbeatInterval, operationCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operationCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!await TryRenewLeaseAsync(
                    messageId,
                    leaseOwner,
                    leaseStore,
                    leaseDuration,
                    clock,
                    leaseRenewalFailedMessage,
                    onLeaseLost,
                    logger,
                    operationCts).ConfigureAwait(false))
            {
                return;
            }
        }
    }
}
