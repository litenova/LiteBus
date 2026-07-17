using System.Threading.Channels;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Publishes transport messages into the in-memory channel broker.
/// </summary>
public sealed class InMemoryPublisher : ITransportPublisher
{
    /// <summary>
    ///     Gets the shared broker receiving published deliveries.
    /// </summary>
    private readonly InMemoryTransportBroker _broker;

    /// <summary>
    ///     Gets the registry that scopes publish resilience by destination.
    /// </summary>
    private readonly ITransportCircuitBreakerRegistry _circuitBreakerRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryPublisher" /> class.
    /// </summary>
    /// <param name="broker">The shared broker receiving published deliveries.</param>
    /// <param name="circuitBreakerRegistry">The registry that scopes publish resilience by destination.</param>
    public InMemoryPublisher(
        InMemoryTransportBroker broker,
        ITransportCircuitBreakerRegistry circuitBreakerRegistry)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(circuitBreakerRegistry);
        _broker = broker;
        _circuitBreakerRegistry = circuitBreakerRegistry;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="ChannelClosedException" /> is handled explicitly so closed destinations increment their circuit.
    ///     Unexpected application failures are traced and rethrown without changing destination resilience state.
    /// </remarks>
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.LiteBusInMemory,
            Destination = request.Destination,
            Route = request.Route,
            MessageId = request.MessageId,
            CorrelationId = request.CorrelationId
        });

        var circuitBreaker = _circuitBreakerRegistry.GetPublisherCircuit(request.Destination);
        TransportCircuitBreakerPermit permit;

        try
        {
            permit = circuitBreaker.AcquirePermit();
        }
        catch (TransportCircuitBreakerOpenException exception)
        {
            TransportTracing.RecordException(activity, exception);
            throw;
        }

        try
        {
            var endpoint = _broker.GetOrCreateEndpoint(request.Destination);

            var delivery = new InMemoryPendingDelivery
            {
                Body = request.Body,
                Headers = CopyHeaders(request.Headers),
                Destination = request.Destination,
                Route = request.Route,
                MessageId = request.MessageId,
                CorrelationId = request.CorrelationId
            };

            await endpoint.EnqueueAsync(delivery, cancellationToken).ConfigureAwait(false);
            circuitBreaker.RecordSuccess(permit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ChannelClosedException exception)
        {
            TransportTracing.RecordException(activity, exception);
            circuitBreaker.RecordFailure(permit);
            throw;
        }
#pragma warning disable CA1031 // Last-resort publish boundary traces unexpected failures before rethrowing.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TransportTracing.RecordException(activity, exception);
            throw;
        }
    }

    /// <summary>
    ///     Copies request headers into a read-only dictionary for delivery handlers.
    /// </summary>
    /// <param name="headers">The optional publish headers.</param>
    /// <returns>A header dictionary, or an empty dictionary when no headers were supplied.</returns>
    private static Dictionary<string, object?> CopyHeaders(IReadOnlyDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(headers, StringComparer.Ordinal);
    }
}
