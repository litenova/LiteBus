namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Consumes transport messages from a destination with manual acknowledgement support.
/// </summary>
public interface IMessageConsumer : IAsyncDisposable
{
    /// <summary>
    ///     Starts the consume loop for the configured destination.
    /// </summary>
    /// <param name="options">The destination and prefetch settings for the subscription.</param>
    /// <param name="handler">
    ///     The handler invoked for each delivery. The handler must call
    ///     <see cref="TransportMessage.AcceptAsync" />, <see cref="TransportMessage.DiscardAsync" />, or
    ///     <see cref="TransportMessage.ReturnToQueueAsync" /> before returning.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel startup.</param>
    /// <returns>A task that completes when the consumer subscription is active.</returns>
    Task StartAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the active consume loop and releases the consumer channel.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel shutdown.</param>
    /// <returns>A task that completes when the consumer has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Waits until the active consume loop stops because of shutdown, cancellation, or channel failure and
    ///     propagates an unexpected terminal failure.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>
    ///     A task that completes when the consumer is no longer active or faults with the terminal consumer failure.
    /// </returns>
    Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default);
}
