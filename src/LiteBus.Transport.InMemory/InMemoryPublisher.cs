using System.Threading.Channels;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Publishes transport messages into the in-memory channel broker.
/// </summary>
public sealed class InMemoryPublisher : IMessageTransport
{
    /// <summary>
    ///     Gets the shared broker receiving published deliveries.
    /// </summary>
    private readonly InMemoryTransportBroker _broker;

    /// <summary>
    ///     Gets the circuit breaker guarding publish operations.
    /// </summary>
    private readonly ITransportCircuitBreaker _circuitBreaker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryPublisher" /> class.
    /// </summary>
    /// <param name="broker">The shared broker receiving published deliveries.</param>
    /// <param name="circuitBreaker">The circuit breaker guarding publish operations.</param>
    public InMemoryPublisher(InMemoryTransportBroker broker, ITransportCircuitBreaker circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        _broker = broker;
        _circuitBreaker = circuitBreaker;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="ChannelClosedException" /> is handled explicitly so closed destinations increment the circuit
    ///     breaker. The final <see cref="Exception" /> handler records any other non-cancellation failure before
    ///     rethrowing.
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

        try
        {
            _circuitBreaker.ThrowIfOpen();
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

            await endpoint.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
            _circuitBreaker.RecordSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TransportCircuitBreakerOpenException exception)
        {
            TransportTracing.RecordException(activity, exception);
            throw;
        }
        catch (ChannelClosedException exception)
        {
            TransportTracing.RecordException(activity, exception);
            TransportPublishFailurePolicy.RecordFailureIfApplicable(_circuitBreaker, exception);
            throw;
        }
#pragma warning disable CA1031 // Last-resort publish boundary records circuit breaker failures before rethrowing.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TransportTracing.RecordException(activity, exception);
            TransportPublishFailurePolicy.RecordFailureIfApplicable(_circuitBreaker, exception);
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
