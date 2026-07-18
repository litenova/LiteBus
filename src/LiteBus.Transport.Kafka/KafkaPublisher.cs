using Confluent.Kafka;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Publishes messages to Kafka topics.
/// </summary>
public sealed class KafkaPublisher : ITransportPublisher, IDisposable
{
    /// <summary>
    ///     Gets the registry that scopes publish resilience by destination.
    /// </summary>
    private readonly ITransportCircuitBreakerRegistry _circuitBreakerRegistry;

    /// <summary>
    ///     Gets the Kafka producer used to publish records.
    /// </summary>
    private readonly IProducer<string, byte[]> _producer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaPublisher" /> class.
    /// </summary>
    /// <param name="producer">The Kafka producer used to publish records.</param>
    /// <param name="circuitBreakerRegistry">The registry that scopes publish resilience by destination.</param>
    public KafkaPublisher(
        IProducer<string, byte[]> producer,
        ITransportCircuitBreakerRegistry circuitBreakerRegistry)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(circuitBreakerRegistry);
        _producer = producer;
        _circuitBreakerRegistry = circuitBreakerRegistry;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="ProduceException{K,V}" /> and <see cref="KafkaException" /> are handled explicitly so broker
    ///     failures increment the destination circuit. Unexpected application failures are traced and rethrown
    ///     without changing broker resilience state.
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
            var message = KafkaMessageMapper.ToKafkaMessage(request);

            await _producer
                .ProduceAsync(request.Destination, message, cancellationToken)
                .ConfigureAwait(false);

            circuitBreaker.RecordSuccess(permit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            circuitBreaker.ReleasePermit(permit);
            throw;
        }
        catch (ProduceException<string, byte[]> exception)
        {
            TransportTracing.RecordException(activity, exception);
            circuitBreaker.RecordFailure(permit);
            throw;
        }
        catch (KafkaException exception)
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
            circuitBreaker.ReleasePermit(permit);
            throw;
        }
    }
}
