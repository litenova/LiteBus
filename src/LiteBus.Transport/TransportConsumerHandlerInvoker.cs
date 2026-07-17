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
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);

        using var activity = TransportTracing.StartConsumeActivity(message);

        try
        {
            await handler(message, cancellationToken).ConfigureAwait(false);
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
