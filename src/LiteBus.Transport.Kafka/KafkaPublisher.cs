using Confluent.Kafka;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Publishes messages to Kafka topics.
/// </summary>
public sealed class KafkaPublisher : IMessageTransport, IDisposable
{
    /// <summary>
    ///     Gets the circuit breaker guarding publish operations.
    /// </summary>
    private readonly ITransportCircuitBreaker _circuitBreaker;

    /// <summary>
    ///     Gets the Kafka producer used to publish records.
    /// </summary>
    private readonly IProducer<string, byte[]> _producer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaPublisher" /> class.
    /// </summary>
    /// <param name="producer">The Kafka producer used to publish records.</param>
    /// <param name="circuitBreaker">The circuit breaker guarding publish operations.</param>
    public KafkaPublisher(IProducer<string, byte[]> producer, ITransportCircuitBreaker circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        _producer = producer;
        _circuitBreaker = circuitBreaker;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="ProduceException{K,V}" /> and <see cref="KafkaException" /> are handled explicitly so broker
    ///     failures increment the circuit breaker. The final <see cref="Exception" /> handler records any other
    ///     non-cancellation failure before rethrowing.
    /// </remarks>
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.Kafka,
            Destination = request.Destination,
            Route = request.Route,
            MessageId = request.MessageId,
            CorrelationId = request.CorrelationId
        });

        try
        {
            _circuitBreaker.ThrowIfOpen();
            var message = KafkaMessageMapper.ToKafkaMessage(request);

            await _producer
                .ProduceAsync(request.Destination, message, cancellationToken)
                .ConfigureAwait(false);

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
        catch (ProduceException<string, byte[]> exception)
        {
            TransportTracing.RecordException(activity, exception);
            _circuitBreaker.RecordFailure();
            throw;
        }
        catch (KafkaException exception)
        {
            TransportTracing.RecordException(activity, exception);
            _circuitBreaker.RecordFailure();
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
}
