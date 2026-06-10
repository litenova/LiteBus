using System;
using System.Threading;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Determines whether a handler exception should negative-acknowledge the delivery.
/// </summary>
internal static class AmqpConsumerAckPolicy
{
    /// <summary>
    ///     Returns a value indicating whether the handler exception should trigger a broker negative acknowledgement.
    /// </summary>
    /// <param name="exception">The exception thrown by the handler.</param>
    /// <param name="cancellationToken">The token supplied to the consume loop.</param>
    /// <returns>
    ///     <see langword="true" /> when the delivery should be nacked; <see langword="false" /> during graceful
    ///     shutdown so the channel close can requeue unacknowledged deliveries.
    /// </returns>
    public static bool ShouldNack(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
}
