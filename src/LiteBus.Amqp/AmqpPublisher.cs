using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace LiteBus.Amqp;

/// <summary>
///     Publishes AMQP messages through a shared connection manager.
/// </summary>
public sealed class AmqpPublisher : IAmqpPublisher
{
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
    }

    /// <inheritdoc />
    public async Task PublishAsync(AmqpPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);
            var properties = CreateBasicProperties(channel, request);

            await channel
                .BasicPublishAsync(
                    request.Exchange,
                    request.RoutingKey,
                    request.Mandatory,
                    properties,
                    request.Body,
                    cancellationToken)
                .ConfigureAwait(false);
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
