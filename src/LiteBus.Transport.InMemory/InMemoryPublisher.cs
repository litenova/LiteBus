using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport;

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
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
    }

    /// <inheritdoc />
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _circuitBreaker.ThrowIfOpen();

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

            await endpoint.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
            _circuitBreaker.RecordSuccess();
        }
        catch (Exception)
        {
            _circuitBreaker.RecordFailure();
            throw;
        }
    }

    /// <summary>
    ///     Copies request headers into a read-only dictionary for delivery handlers.
    /// </summary>
    /// <param name="headers">The optional publish headers.</param>
    /// <returns>A header dictionary, or an empty dictionary when no headers were supplied.</returns>
    private static IReadOnlyDictionary<string, object?> CopyHeaders(IReadOnlyDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(headers, StringComparer.Ordinal);
    }
}

