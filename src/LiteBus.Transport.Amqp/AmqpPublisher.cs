using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Publishes AMQP messages through a shared connection manager.
/// </summary>
public sealed class AmqpPublisher : IAmqpPublisher, ITransportPublisher, IAsyncDisposable
{
    /// <summary>
    ///     Gets the registry that scopes publish resilience by exchange or default-exchange route.
    /// </summary>
    private readonly ITransportCircuitBreakerRegistry _circuitBreakerRegistry;

    /// <summary>
    ///     Gets the connection manager used to open publish channels.
    /// </summary>
    private readonly IAmqpConnectionManager _connectionManager;

    /// <summary>
    ///     Serializes publish operations on the shared publish channel.
    /// </summary>
    private readonly SemaphoreSlim _publishGate = new(1, 1);

    /// <summary>
    ///     Tracks whether asynchronous disposal has started.
    /// </summary>
    private int _disposeState;

    /// <summary>
    ///     Gets the lazily created channel reused for publications.
    /// </summary>
    private IChannel? _publishChannel;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpPublisher" /> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager used to open publish channels.</param>
    /// <param name="circuitBreakerRegistry">
    ///     The registry that scopes publish resilience by exchange or default-exchange route.
    /// </param>
    public AmqpPublisher(
        IAmqpConnectionManager connectionManager,
        ITransportCircuitBreakerRegistry circuitBreakerRegistry)
    {
        ArgumentNullException.ThrowIfNull(connectionManager);
        ArgumentNullException.ThrowIfNull(circuitBreakerRegistry);
        _connectionManager = connectionManager;
        _circuitBreakerRegistry = circuitBreakerRegistry;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="OperationInterruptedException" />, <see cref="PublishException" />, and
    ///     <see cref="BrokerUnreachableException" /> are handled explicitly so broker failures increment the exchange
    ///     circuit. Unexpected application failures are traced and rethrown without changing broker resilience state.
    /// </remarks>
    public async Task PublishAsync(AmqpPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Exchange);
        ArgumentNullException.ThrowIfNull(request.RoutingKey);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.RabbitMq,
            Destination = request.Exchange,
            Route = request.RoutingKey,
            MessageId = request.MessageId,
            CorrelationId = request.CorrelationId
        });

        var circuitBreaker = _circuitBreakerRegistry.GetPublisherCircuit(GetCircuitDestination(request));
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

        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

            var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);
            var properties = CreateBasicProperties(channel, request);

            try
            {
                await channel
                    .BasicPublishAsync(
                        request.Exchange,
                        request.RoutingKey,
                        request.Mandatory,
                        properties,
                        request.Body,
                        cancellationToken)
                    .ConfigureAwait(false);

                circuitBreaker.RecordSuccess(permit);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationInterruptedException exception)
            {
                TransportTracing.RecordException(activity, exception);
                circuitBreaker.RecordFailure(permit);
                throw;
            }
            catch (PublishException exception)
            {
                TransportTracing.RecordException(activity, exception);
                circuitBreaker.RecordFailure(permit);
                throw;
            }
            catch (BrokerUnreachableException exception)
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
        finally
        {
            _publishGate.Release();
        }
    }

    /// <inheritdoc />
    public Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = request.Destination,
                RoutingKey = request.Route ?? string.Empty,
                Body = request.Body,
                ContentType = request.ContentType,
                ContentEncoding = request.ContentEncoding,
                Persistent = request.Persistent,
                Mandatory = request.Mandatory,
                MessageId = request.MessageId,
                CorrelationId = request.CorrelationId,
                Headers = request.Headers
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        await _publishGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_publishChannel is not null)
            {
                await _publishChannel.DisposeAsync().ConfigureAwait(false);
                _publishChannel = null;
            }
        }
        finally
        {
            _publishGate.Release();
        }
    }

    /// <summary>
    ///     Gets the reusable publish channel, creating it when needed.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel channel creation.</param>
    /// <returns>The open publish channel.</returns>
    private async Task<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is { IsOpen: true })
        {
            return _publishChannel;
        }

        if (_publishChannel is not null)
        {
            await _publishChannel.DisposeAsync().ConfigureAwait(false);
            _publishChannel = null;
        }

        _publishChannel = await _connectionManager.CreatePublisherChannelAsync(cancellationToken).ConfigureAwait(false);
        return _publishChannel;
    }

    /// <summary>
    ///     Creates AMQP message properties from the publish request.
    /// </summary>
    /// <param name="channel">The channel used to allocate message properties.</param>
    /// <param name="request">The publish request containing body metadata and headers.</param>
    /// <returns>The AMQP basic properties for the publication.</returns>
    private static BasicProperties CreateBasicProperties(IChannel channel, AmqpPublishRequest request)
    {
        var properties = new BasicProperties
        {
            ContentType = request.ContentType,
            ContentEncoding = request.ContentEncoding,
            DeliveryMode = request.Persistent ? DeliveryModes.Persistent : DeliveryModes.Transient,
            MessageId = request.MessageId,
            CorrelationId = request.CorrelationId,
            Headers = CopyHeaders(request.Headers)
        };

        return properties;
    }

    /// <summary>
    ///     Gets the non-empty publisher circuit scope for an AMQP publication.
    /// </summary>
    /// <param name="request">The publication request containing the exchange and routing key.</param>
    /// <returns>The exchange scope, or the routing-key scope when publishing through the default exchange.</returns>
    /// <remarks>
    ///     AMQP reserves the empty exchange name for the default exchange. Its routing key identifies the destination
    ///     queue, so default-exchange publications must not share one unnamed circuit or fail destination validation.
    /// </remarks>
    private static string GetCircuitDestination(AmqpPublishRequest request)
    {
        return request.Exchange.Length == 0
            ? string.Concat("amqp:default:", request.RoutingKey)
            : string.Concat("amqp:exchange:", request.Exchange);
    }

    /// <summary>
    ///     Copies request headers into a mutable dictionary suitable for AMQP message properties.
    /// </summary>
    /// <param name="headers">The optional request headers.</param>
    /// <returns>A header dictionary, or <see langword="null" /> when no headers were supplied.</returns>
    private static Dictionary<string, object?>? CopyHeaders(IReadOnlyDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, object?>(headers, StringComparer.Ordinal);
    }
}
