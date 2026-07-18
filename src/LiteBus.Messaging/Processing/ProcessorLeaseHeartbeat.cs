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
    /// <param name="context">The lease renewal inputs shared across heartbeat attempts.</param>
    /// <param name="operation">The dispatch work executed while renewals continue.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>The value returned by <paramref name="operation" />.</returns>
    public static async Task<T> RunWithHeartbeatAsync<T>(
        LeaseHeartbeatContext context,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return await RunWithHeartbeatAsync(
                context,
                (heartbeatToken, _) => operation(heartbeatToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs processor work with lease renewal while exposing both the heartbeat token and the original shutdown token.
    /// </summary>
    /// <typeparam name="T">The result type returned by the operation.</typeparam>
    /// <param name="context">The lease renewal inputs shared across heartbeat attempts.</param>
    /// <param name="operation">The work executed with the heartbeat and original shutdown tokens.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch and stop renewals.</param>
    /// <returns>The value returned by <paramref name="operation" />.</returns>
    public static async Task<T> RunWithHeartbeatAsync<T>(
        LeaseHeartbeatContext context,
        Func<CancellationToken, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.LeaseStore);
        ArgumentNullException.ThrowIfNull(context.Clock);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(context.ProcessorName);

        if (context.HeartbeatInterval <= TimeSpan.Zero)
        {
            return await operation(cancellationToken, cancellationToken).ConfigureAwait(false);
        }

        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseWasLost = false;
        var heartbeatContext = context with
        {
            OnLeaseLost = () =>
            {
                leaseWasLost = true;
                context.OnLeaseLost?.Invoke();
            }
        };

        if (!await TryRenewLeaseAsync(heartbeatContext, operationCts).ConfigureAwait(false))
        {
            throw new OperationCanceledException(operationCts.Token);
        }

        var heartbeatTask = RenewLoopAsync(heartbeatContext, operationCts, cancellationToken);

        try
        {
            var result = await operation(operationCts.Token, cancellationToken).ConfigureAwait(false);

            if (leaseWasLost)
            {
                throw new OperationCanceledException(operationCts.Token);
            }

            return result;
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
                new LeaseRenewalRequest(
                    context.MessageId,
                    context.LeaseOwner,
                    context.LeaseGeneration,
                    context.LeaseDuration,
                    expiresAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (renewed)
        {
            return true;
        }

        if (context.Logger is not null)
        {
            MessageProcessorLogMessages.LeaseRenewalFailed(
                context.Logger,
                context.ProcessorName,
                context.MessageId,
                context.LeaseOwner);
        }

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
