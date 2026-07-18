namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Publishes messages to a transport broker.
/// </summary>
public interface ITransportPublisher
{
    /// <summary>
    ///     Publishes one message to the configured broker.
    /// </summary>
    /// <param name="request">The publication request describing destination, route, body, and headers.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that completes when the broker accepts the message.</returns>
    Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default);
}