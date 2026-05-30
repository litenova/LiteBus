using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Amqp;

/// <summary>
///     Consumes AMQP messages from a queue with manual acknowledgement support.
/// </summary>
public interface IAmqpConsumer : IAsyncDisposable
{
    /// <summary>
    ///     Starts the consume loop for the configured queue.
    /// </summary>
    /// <param name="options">The queue and prefetch settings for the subscription.</param>
    /// <param name="handler">
    ///     The handler invoked for each delivery. The handler must call
    ///     <see cref="AmqpReceivedMessage.AckAsync" /> or <see cref="AmqpReceivedMessage.NackAsync" /> before returning.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel startup.</param>
    /// <returns>A task that completes when the consumer subscription is active.</returns>
    Task StartAsync(
        AmqpConsumerOptions options,
        Func<AmqpReceivedMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the active consume loop and releases the consumer channel.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel shutdown.</param>
    /// <returns>A task that completes when the consumer has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
