using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Publishes messages to Amazon SQS queues.
/// </summary>
public sealed class SqsPublisher : IMessageTransport
{
    /// <summary>
    ///     Gets the circuit breaker guarding publish operations.
    /// </summary>
    private readonly ITransportCircuitBreaker _circuitBreaker;

    /// <summary>
    ///     Gets the SQS client used to send messages.
    /// </summary>
    private readonly IAmazonSQS _sqsClient;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqsPublisher" /> class.
    /// </summary>
    /// <param name="sqsClient">The SQS client used to send messages.</param>
    /// <param name="circuitBreaker">The circuit breaker guarding publish operations.</param>
    public SqsPublisher(IAmazonSQS sqsClient, ITransportCircuitBreaker circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(sqsClient);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        _sqsClient = sqsClient;
        _circuitBreaker = circuitBreaker;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="AmazonSQSException" /> is handled explicitly so broker failures increment the circuit breaker.
    ///     The final <see cref="Exception" /> handler records any other non-cancellation failure before rethrowing.
    /// </remarks>
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _circuitBreaker.ThrowIfOpen();

        try
        {
            var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

            await _sqsClient.SendMessageAsync(sendRequest, cancellationToken).ConfigureAwait(false);

            _circuitBreaker.RecordSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonSQSException)
        {
            _circuitBreaker.RecordFailure();
            throw;
        }
#pragma warning disable CA1031 // Last-resort publish boundary records circuit breaker failures before rethrowing.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TransportPublishFailurePolicy.RecordFailureIfApplicable(_circuitBreaker, exception);
            throw;
        }
    }
}
