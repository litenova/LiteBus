namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Bundles broker acknowledgement delegates passed from a consumer into a transport message mapper.
/// </summary>
/// <remarks>
///     <para>
///         Consumers construct these handlers from broker-specific operations (delete, complete, commit, abandon,
///         change visibility, seek, and similar). Mappers assign them to <see cref="TransportMessage.AckAsync" /> and
///         <see cref="TransportMessage.NackAsync" /> without accepting multiple delegate parameters.
///     </para>
///     <para>
///         The negative-acknowledgement delegate receives a requeue flag. When <see langword="true" />, the broker
///         should return the delivery for redelivery; otherwise the delivery is discarded or dead-lettered.
///     </para>
/// </remarks>
public sealed record TransportConsumerAckHandlers
{
    /// <summary>
    ///     Gets the delegate that acknowledges successful processing of the delivery.
    /// </summary>
    public required Func<CancellationToken, Task> AckAsync { get; init; }

    /// <summary>
    ///     Gets the delegate that negative-acknowledges the delivery.
    /// </summary>
    /// <remarks>
    ///     The delegate receives a requeue flag. When <see langword="true" />, the broker should return the message
    ///     for redelivery; otherwise the message is discarded or dead-lettered.
    /// </remarks>
    public required Func<bool, CancellationToken, Task> NackAsync { get; init; }
}