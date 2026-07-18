using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Invokes transport consumer handlers with default requeue behavior on unhandled failures.
/// </summary>
/// <remarks>
///     When a handler throws a non-cancellation exception, the invoker calls
///     <see cref="TransportMessage.ReturnToQueueAsync" /> so the broker redelivers the message and the consume loop
///     continues. Raw <see cref="IMessageConsumer" /> hosts rely on this behavior; inbox ingress applies its own
///     poison and retry policy on top of transport deliveries.
/// </remarks>
public static class TransportConsumerHandlerInvoker
{
    /// <summary>
    ///     Creates a handler that limits concurrent invocations before applying transport settlement behavior.
    /// </summary>
    /// <param name="handler">The consumer handler to invoke.</param>
    /// <param name="maxInFlightMessages">The maximum number of handler invocations admitted concurrently.</param>
    /// <returns>A handler with subscription-scoped admission control.</returns>
    public static Func<TransportMessage, CancellationToken, Task> CreateBoundedHandler(
        Func<TransportMessage, CancellationToken, Task> handler,
        int maxInFlightMessages)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInFlightMessages, 1);

        var admission = new SemaphoreSlim(maxInFlightMessages, maxInFlightMessages);

        return async (message, cancellationToken) =>
        {
            await admission.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await InvokeAsync(message, handler, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                admission.Release();
            }
        };
    }

    /// <summary>
    ///     Creates an outcome-reporting handler that limits concurrent invocations before transport settlement.
    /// </summary>
    /// <param name="handler">The consumer handler to invoke.</param>
    /// <param name="maxInFlightMessages">The maximum number of handler invocations admitted concurrently.</param>
    /// <returns>An outcome-reporting handler with subscription-scoped admission control.</returns>
    public static Func<TransportMessage, CancellationToken, Task<TransportConsumerInvocationOutcome>>
        CreateBoundedOutcomeHandler(
            Func<TransportMessage, CancellationToken, Task> handler,
            int maxInFlightMessages)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInFlightMessages, 1);

        var admission = new SemaphoreSlim(maxInFlightMessages, maxInFlightMessages);

        return async (message, cancellationToken) =>
        {
            await admission.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await InvokeWithOutcomeAsync(message, handler, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                admission.Release();
            }
        };
    }

    /// <summary>
    ///     Invokes the handler and returns the message to the queue when processing fails unexpectedly.
    /// </summary>
    /// <param name="message">The transport delivery passed to the handler.</param>
    /// <param name="handler">The handler invoked for the delivery.</param>
    /// <param name="cancellationToken">The token used to cancel handler execution.</param>
    /// <returns>A task that completes when the handler finishes or the message has been requeued.</returns>
    public static async Task InvokeAsync(
        TransportMessage message,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        _ = await InvokeWithOutcomeAsync(message, handler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Invokes a handler and reports whether the invoker returned a failed delivery to the broker.
    /// </summary>
    /// <param name="message">The transport delivery passed to the handler.</param>
    /// <param name="handler">The handler invoked for the delivery.</param>
    /// <param name="cancellationToken">The token used to cancel handler execution.</param>
    /// <returns>The settlement outcome observed by the caller.</returns>
    public static async Task<TransportConsumerInvocationOutcome> InvokeWithOutcomeAsync(
        TransportMessage message,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);

        using var activity = TransportTracing.StartConsumeActivity(message);

        try
        {
            await handler(message, cancellationToken).ConfigureAwait(false);
            return TransportConsumerInvocationOutcome.Handled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Handler failures are intentionally broad; requeue policy filters graceful shutdown.
        catch (Exception exception) when (ShouldRequeueOnFailure(exception, cancellationToken))
#pragma warning restore CA1031
        {
            TransportTracing.RecordException(activity, exception);
            await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
            return TransportConsumerInvocationOutcome.Requeued;
        }
    }

    /// <summary>
    ///     Returns a value indicating whether a handler exception should trigger broker redelivery.
    /// </summary>
    /// <param name="exception">The exception thrown by the handler.</param>
    /// <param name="cancellationToken">The token supplied to the consume loop.</param>
    /// <returns>
    ///     <see langword="true" /> when the delivery should be returned for redelivery; <see langword="false" /> during
    ///     graceful shutdown so unacknowledged deliveries follow broker-specific close semantics.
    /// </returns>
    public static bool ShouldRequeueOnFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
    }
}
