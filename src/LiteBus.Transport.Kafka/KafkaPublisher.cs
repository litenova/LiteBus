using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Publishes messages to Kafka topics.
/// </summary>
public sealed class KafkaPublisher : IMessageTransport, IDisposable
{
    /// <summary>
    ///     Gets the Kafka producer used to publish records.
    /// </summary>
    private readonly IProducer<string, byte[]> _producer;

    /// <summary>
    ///     Gets the circuit breaker guarding publish operations.
    /// </summary>
    private readonly ITransportCircuitBreaker _circuitBreaker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaPublisher" /> class.
    /// </summary>
    /// <param name="producer">The Kafka producer used to publish records.</param>
    /// <param name="circuitBreaker">The circuit breaker guarding publish operations.</param>
    public KafkaPublisher(IProducer<string, byte[]> producer, ITransportCircuitBreaker circuitBreaker)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
    }

    /// <inheritdoc />
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _circuitBreaker.ThrowIfOpen();

        try
        {
            var message = KafkaMessageMapper.ToKafkaMessage(request);
            await _producer
                .ProduceAsync(request.Destination, message, cancellationToken)
                .ConfigureAwait(false);
            _circuitBreaker.RecordSuccess();
        }
        catch (Exception)
        {
            _circuitBreaker.RecordFailure();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

