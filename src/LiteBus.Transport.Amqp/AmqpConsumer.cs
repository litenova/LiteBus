using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Consumes AMQP deliveries from one queue with manual acknowledgement support.
/// </summary>
public sealed class AmqpConsumer : IAmqpConsumer, IMessageConsumer
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
    ///     Signals when the active consume loop stops because of shutdown, cancellation, or channel failure.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

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
    public Task StartAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        return StartAsync(
            new AmqpConsumerOptions
            {
                QueueName = options.Destination,
                PrefetchCount = options.PrefetchCount,
                DeclareQueue = options.DeclareDestination,
                DurableQueue = options.DurableDestination,
                Exclusive = options.Exclusive,
                ConsumerTag = options.ConsumerTag,
                QueueArguments = options.DestinationArguments
            },
            (message, token) => handler(ToTransportMessage(message), token),
            cancellationToken);
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
                throw new Exceptions.AmqpTransportConfigurationException("The AMQP consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _consumerChannel = await _connectionManager.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
            _consumerChannel.ChannelShutdownAsync += OnChannelShutdownAsync;

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
                if (_consumerChannel is null)
                {
                    return;
                }

                try
                {
                    var message = CreateReceivedMessage(_consumerChannel, delivery);
                    await handler(message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (_consumerChannel.IsOpen)
                    {
                        try
                        {
                            await _consumerChannel
                                .BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (AlreadyClosedException)
                        {
                            SignalStopped();
                        }
                    }
                }
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
                SignalStopped();
                return;
            }

            _consumerChannel.ChannelShutdownAsync -= OnChannelShutdownAsync;

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
            SignalStopped();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default) =>
        _stoppedTcs.Task.WaitAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    /// <summary>
    ///     Maps an AMQP delivery to the transport-neutral message model.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The transport message passed to generic consumer handlers.</returns>
    private static TransportMessage ToTransportMessage(AmqpReceivedMessage message) =>
        new()
        {
            Body = message.Body,
            Headers = message.Headers,
            Destination = message.Exchange,
            Route = message.RoutingKey,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            Redelivered = message.Redelivered,
            AckAsync = message.AcceptAsync,
            NackAsync = (requeue, token) => message.NackDelegate(false, requeue, token)
        };

    /// <summary>
    ///     Creates a new task source used to observe consumer shutdown.
    /// </summary>
    /// <returns>The task source for the current consume session.</returns>
    private static TaskCompletionSource CreateStoppedTaskSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Marks the current consume session as stopped.
    /// </summary>
    private void SignalStopped() => _stoppedTcs.TrySetResult();

    /// <summary>
    ///     Handles broker-initiated channel shutdown so callers can restart the consumer.
    /// </summary>
    /// <param name="sender">The channel that shut down.</param>
    /// <param name="args">The shutdown details supplied by the broker.</param>
    /// <returns>A completed task.</returns>
    private Task OnChannelShutdownAsync(object? sender, ShutdownEventArgs args)
    {
        SignalStopped();
        return Task.CompletedTask;
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
            AckDelegate = async (multiple, token) =>
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple, token).ConfigureAwait(false);
            },
            NackDelegate = async (multiple, requeue, token) =>
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
