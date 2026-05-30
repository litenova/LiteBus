using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace LiteBus.Amqp;

/// <summary>
///     Consumes AMQP deliveries from one queue with manual acknowledgement support.
/// </summary>
public sealed class AmqpConsumer : IAmqpConsumer
{
    /// <summary>
    ///     Gets the connection manager used to open the consumer channel.
    /// </summary>
    private readonly IAmqpConnectionManager _connectionManager;

    /// <summary>
    ///     Serializes start and stop operations on the consumer channel.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    ///     Gets the active consumer channel, if the consume loop has started.
    /// </summary>
    private IChannel? _consumerChannel;

    /// <summary>
    ///     Gets the broker-assigned consumer tag for the active subscription.
    /// </summary>
    private string? _consumerTag;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpConsumer" /> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager used to open the consumer channel.</param>
    public AmqpConsumer(IAmqpConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    /// <inheritdoc />
    public async Task StartAsync(
        AmqpConsumerOptions options,
        Func<AmqpReceivedMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumerChannel is not null)
            {
                throw new InvalidOperationException("The AMQP consumer is already started.");
            }

            _consumerChannel = await _connectionManager.CreateChannelAsync(cancellationToken).ConfigureAwait(false);

            if (options.DeclareQueue)
            {
                await _consumerChannel
                    .QueueDeclareAsync(
                        queue: options.QueueName,
                        durable: options.DurableQueue,
                        exclusive: false,
                        autoDelete: false,
                        arguments: CopyQueueArguments(options.QueueArguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (options.PrefetchCount > 0)
            {
                await _consumerChannel
                    .BasicQosAsync(prefetchSize: 0, prefetchCount: options.PrefetchCount, global: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                var message = CreateReceivedMessage(_consumerChannel, delivery);
                await handler(message, cancellationToken).ConfigureAwait(false);
            };

            _consumerTag = await _consumerChannel
                .BasicConsumeAsync(
                    queue: options.QueueName,
                    autoAck: false,
                    consumerTag: options.ConsumerTag ?? string.Empty,
                    noLocal: false,
                    exclusive: options.Exclusive,
                    arguments: null,
                    consumer: consumer,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumerChannel is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_consumerTag))
            {
                if (_consumerChannel.IsOpen)
                {
                    try
                    {
                        await _consumerChannel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (AlreadyClosedException)
                    {
                        // The broker or caller already closed the shared connection.
                    }
                }

                _consumerTag = null;
            }

            await _consumerChannel.DisposeAsync().ConfigureAwait(false);
            _consumerChannel = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    /// <summary>
    ///     Creates the public delivery model exposed to consumer handlers.
    /// </summary>
    /// <param name="channel">The channel used to acknowledge the delivery.</param>
    /// <param name="delivery">The broker delivery event arguments.</param>
    /// <returns>The received message with acknowledgement delegates attached.</returns>
    private static AmqpReceivedMessage CreateReceivedMessage(IChannel channel, BasicDeliverEventArgs delivery)
    {
        return new AmqpReceivedMessage
        {
            Body = delivery.Body,
            Headers = CopyHeaders(delivery.BasicProperties.Headers),
            DeliveryTag = delivery.DeliveryTag,
            Exchange = delivery.Exchange,
            RoutingKey = delivery.RoutingKey,
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            Redelivered = delivery.Redelivered,
            AckAsync = async (multiple, token) =>
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple, token).ConfigureAwait(false);
            },
            NackAsync = async (requeue, multiple, token) =>
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple, requeue, token).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    ///     Copies broker headers into a read-only dictionary for handlers.
    /// </summary>
    /// <param name="headers">The optional AMQP headers dictionary.</param>
    /// <returns>A read-only header dictionary.</returns>
    private static IReadOnlyDictionary<string, object?> CopyHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(headers, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Copies queue declaration arguments into a mutable dictionary when supplied.
    /// </summary>
    /// <param name="arguments">The optional queue arguments.</param>
    /// <returns>A queue argument dictionary, or <see langword="null" /> when none were supplied.</returns>
    private static Dictionary<string, object?>? CopyQueueArguments(IReadOnlyDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
    }
}
