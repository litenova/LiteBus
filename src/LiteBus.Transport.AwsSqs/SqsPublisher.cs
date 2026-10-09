using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs.Exceptions;

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
    ///     Client-side SDK failures that never reached the broker surface as
    ///     <see cref="AwsSqsTransportConfigurationException" /> and release the permit instead of recording a
    ///     failure. Unexpected application failures are traced and rethrown without changing broker resilience state.
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
        catch (AmazonClientException exception) when (IsConfigurationFailure(exception))
        {
            var configurationException = new AwsSqsTransportConfigurationException(
                $"The AWS SQS publish to '{request.Destination}' failed with a client-side error that does not " +
                "indicate broker health. Configure the region explicitly or make it resolvable from the " +
                "environment, shared configuration file, or instance metadata.",
                exception);
            TransportTracing.RecordException(activity, configurationException);
            circuitBreaker.ReleasePermit(permit);
            throw configurationException;
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

    /// <summary>
    ///     Returns whether a publish failure reflects client configuration rather than broker health.
    /// </summary>
    /// <remarks>
    ///     Service responses, including <see cref="AmazonSQSException" />, record a circuit-breaker failure. Only
    ///     client-side SDK failures that never reached the broker bypass it.
    /// </remarks>
    /// <param name="exception">The exception thrown by the publish operation.</param>
    /// <returns><see langword="true" /> for client-side SDK failures; otherwise, <see langword="false" />.</returns>
    internal static bool IsConfigurationFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is AmazonClientException && exception is not AmazonServiceException;
    }
}
