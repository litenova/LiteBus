using System;
using System.Threading;
using System.Threading.Tasks;
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
    /// <param name="context">The lease renewal inputs shared across heartbeat attempts.</param>
    /// <param name="operation">The dispatch work executed while renewals continue.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>The value returned by <paramref name="operation" />.</returns>
    public static async Task<T> RunWithHeartbeatAsync<T>(
        LeaseHeartbeatContext context,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.LeaseStore);
        ArgumentNullException.ThrowIfNull(context.Clock);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(context.LeaseRenewalFailedMessage);

        if (context.HeartbeatInterval <= TimeSpan.Zero)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (!await TryRenewLeaseAsync(context, operationCts).ConfigureAwait(false))
        {
            throw new OperationCanceledException(operationCts.Token);
        }

        var heartbeatTask = RenewLoopAsync(context, operationCts, cancellationToken);

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
    /// <param name="context">The lease renewal inputs shared across heartbeat attempts.</param>
    /// <param name="operationCts">The linked token source used to cancel dispatch when the lease is lost.</param>
    /// <returns><see langword="true" /> when the lease was renewed; otherwise <see langword="false" />.</returns>
    private static async Task<bool> TryRenewLeaseAsync(
        LeaseHeartbeatContext context,
        CancellationTokenSource operationCts)
    {
        var expiresAt = context.Clock.GetUtcNow().Add(context.LeaseDuration);

        var renewed = await context.LeaseStore.RenewLeaseAsync(
                context.MessageId,
                context.LeaseOwner,
                expiresAt,
                CancellationToken.None)
            .ConfigureAwait(false);

        if (renewed)
        {
            return true;
        }

        context.Logger?.LogWarning(
            context.LeaseRenewalFailedMessage,
            context.MessageId,
            context.LeaseOwner);

        context.OnLeaseLost?.Invoke();
        await operationCts.CancelAsync().ConfigureAwait(false);
        return false;
    }

    /// <summary>
    ///     Extends the lease on a fixed interval until cancellation is requested.
    /// </summary>
    /// <param name="context">The lease renewal inputs shared across heartbeat attempts.</param>
    /// <param name="operationCts">The linked token source used to cancel dispatch when the lease is lost.</param>
    /// <param name="cancellationToken">A token that stops the renewal loop.</param>
    /// <returns>A task that completes when the loop is canceled.</returns>
    private static async Task RenewLoopAsync(
        LeaseHeartbeatContext context,
        CancellationTokenSource operationCts,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !operationCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(context.HeartbeatInterval, operationCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operationCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!await TryRenewLeaseAsync(context, operationCts).ConfigureAwait(false))
            {
                return;
            }
        }
    }
}