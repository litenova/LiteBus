using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Publishes messages to Amazon SQS queues.
/// </summary>
public sealed class SqsPublisher : ITransportPublisher
{
    /// <summary>
    ///     Gets the registry that scopes publish resilience by destination.
    /// </summary>
    private readonly ITransportCircuitBreakerRegistry _circuitBreakerRegistry;

    /// <summary>
    ///     Gets the SQS client used to send messages.
    /// </summary>
    private readonly IAmazonSQS _sqsClient;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqsPublisher" /> class.
    /// </summary>
    /// <param name="sqsClient">The SQS client used to send messages.</param>
    /// <param name="circuitBreakerRegistry">The registry that scopes publish resilience by destination.</param>
    public SqsPublisher(
        IAmazonSQS sqsClient,
        ITransportCircuitBreakerRegistry circuitBreakerRegistry)
    {
        ArgumentNullException.ThrowIfNull(sqsClient);
        ArgumentNullException.ThrowIfNull(circuitBreakerRegistry);
        _sqsClient = sqsClient;
        _circuitBreakerRegistry = circuitBreakerRegistry;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="AmazonSQSException" /> is handled explicitly so broker failures increment the destination circuit.
    ///     Unexpected application failures are traced and rethrown without changing broker resilience state.
    /// </remarks>
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.AmazonSqs,
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
            var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

            await _sqsClient.SendMessageAsync(sendRequest, cancellationToken).ConfigureAwait(false);

            circuitBreaker.RecordSuccess(permit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            circuitBreaker.ReleasePermit(permit);
            throw;
        }
        catch (AmazonSQSException exception)
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
