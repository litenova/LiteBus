using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Defines transport-independent safety limits applied before inbox acceptance.
/// </summary>
/// <remarks>
///     Broker adapters expose this value object so payload size, identity trust, and batch admission do not silently
///     vary by provider. Broker-specific options remain on their adapter types.
/// </remarks>
public sealed record TransportInboxIngressSafetyOptions
{
    /// <summary>
    ///     Gets the default maximum body size accepted by ingress.
    /// </summary>
    public const int DefaultMaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>
    ///     Gets the default maximum number of ingress handlers that may execute concurrently.
    /// </summary>
    public const int DefaultMaxInFlightMessages = TransportConsumerOptions.DefaultMaxInFlightMessages;

    /// <summary>
    ///     Gets the default number of deliveries accepted in one inbox batch.
    /// </summary>
    public const int DefaultBatchSize = 10;

    /// <summary>
    ///     Gets the maximum message body size accepted before deserialization.
    /// </summary>
    public int MaxMessageBytes { get; init; } = DefaultMaxMessageBytes;

    /// <summary>
    ///     Gets a value indicating whether a stable broker delivery identity is required.
    /// </summary>
    public bool RequireStableIdentity { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether trusted application headers may override broker identity and tenant metadata.
    /// </summary>
    public bool TrustApplicationHeaders { get; init; }

    /// <summary>
    ///     Gets an optional callback invoked before deserialization to authorize or reject a delivery.
    /// </summary>
    /// <value>A callback that may throw to apply the configured requeue or discard policy.</value>
    public Func<TransportMessage, CancellationToken, Task>? AuthorizeDeliveryAsync { get; init; }

    /// <summary>
    ///     Gets the maximum number of ingress delivery handlers that LiteBus may execute concurrently.
    /// </summary>
    /// <value>Default is <see cref="DefaultMaxInFlightMessages" />.</value>
    public int MaxInFlightMessages { get; init; } = DefaultMaxInFlightMessages;

    /// <summary>
    ///     Gets a value indicating whether deliveries are accepted in batches.
    /// </summary>
    public bool EnableBatchAccept { get; init; }

    /// <summary>
    ///     Gets the number of buffered deliveries accepted in one inbox batch.
    /// </summary>
    /// <value>Default is <see cref="DefaultBatchSize" />.</value>
    public int BatchSize { get; init; } = DefaultBatchSize;

    /// <summary>
    ///     Gets the maximum wait before a partial batch is accepted.
    /// </summary>
    public TimeSpan BatchMaxWait { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Validates provider-neutral ingress bounds before the broker consumer is registered.
    /// </summary>
    /// <exception cref="LiteBusConfigurationException">A configured numeric bound is outside its supported range.</exception>
    public void Validate()
    {
        if (MaxMessageBytes < 0)
        {
            throw new LiteBusConfigurationException(
                $"{nameof(MaxMessageBytes)} must be greater than or equal to zero.");
        }

        if (MaxInFlightMessages < 1)
        {
            throw new LiteBusConfigurationException(
                $"{nameof(MaxInFlightMessages)} must be greater than zero.");
        }

        if (BatchSize < 1)
        {
            throw new LiteBusConfigurationException(
                $"{nameof(BatchSize)} must be greater than zero.");
        }

        if (BatchMaxWait < TimeSpan.Zero)
        {
            throw new LiteBusConfigurationException(
                $"{nameof(BatchMaxWait)} must be greater than or equal to zero.");
        }
    }
}
