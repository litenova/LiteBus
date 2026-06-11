using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;
using RabbitMQ.Client;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Publishes AMQP messages through a shared connection manager.
/// </summary>
public sealed class AmqpPublisher : IAmqpPublisher, IMessageTransport
{
    /// <summary>
    ///     Gets the circuit breaker shared with the connection manager when it is a <see cref="AmqpConnectionManager" />.
    /// </summary>
    private readonly ITransportCircuitBreaker? _circuitBreaker;

    /// <summary>
    ///     Gets the connection manager used to open publish channels.
    /// </summary>
    private readonly IAmqpConnectionManager _connectionManager;

    /// <summary>
    ///     Serializes publish operations on the shared publish channel.
    /// </summary>
    private readonly SemaphoreSlim _publishGate = new(1, 1);

    /// <summary>
    ///     Gets the lazily created channel reused for publications.
    /// </summary>
    private IChannel? _publishChannel;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpPublisher" /> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager used to open publish channels.</param>
    public AmqpPublisher(IAmqpConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

        _circuitBreaker = connectionManager is AmqpConnectionManager manager
            ? manager.TransportCircuitBreaker
            : null;
    }

    /// <inheritdoc />
    public async Task PublishAsync(AmqpPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _circuitBreaker?.ThrowIfOpen();

        using var activity = TransportTracing.StartPublishActivity(
            request.Exchange,
            request.RoutingKey,
            request.MessageId);

        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
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

                _circuitBreaker?.RecordSuccess();
            }
            catch (Exception)
            {
                _circuitBreaker?.RecordFailure();
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

        _publishChannel = await _connectionManager.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
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