using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp.Exceptions;
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
    ///     Gets the active consumer channel, if the consume loop has started.
    /// </summary>
    private IChannel? _consumerChannel;

    /// <summary>
    ///     Gets the broker-assigned consumer tag for the active subscription.
    /// </summary>
    private string? _consumerTag;

    /// <summary>
    ///     Gets the queue name for the active subscription when consuming through <see cref="IMessageConsumer" />.
    /// </summary>
    private string? _activeQueueName;

    /// <summary>
    ///     Signals when the active consume loop stops because of shutdown, cancellation, or channel failure.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpConsumer" /> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager used to open the consumer channel.</param>
    public AmqpConsumer(IAmqpConnectionManager connectionManager)
    {
        ArgumentNullException.ThrowIfNull(connectionManager);
        _connectionManager = connectionManager;
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
                throw new AmqpTransportConfigurationException("The AMQP consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _activeQueueName = options.QueueName;
            _consumerChannel = await _connectionManager.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
            _consumerChannel.ChannelShutdownAsync += OnChannelShutdownAsync;

            if (options.DeclareQueue)
            {
                await _consumerChannel
                    .QueueDeclareAsync(
                        options.QueueName,
                        options.DurableQueue,
                        false,
                        false,
                        CopyQueueArguments(options.QueueArguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (options.PrefetchCount > 0)
            {
                await _consumerChannel
                    .BasicQosAsync(0, options.PrefetchCount, false, cancellationToken)
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Graceful shutdown: do not nack so unacknowledged deliveries follow channel-close semantics.
                }
#pragma warning disable CA1031 // Handler failures are intentionally broad; nack policy filters graceful shutdown.
                catch (Exception exception) when (AmqpConsumerAckPolicy.ShouldNack(exception, cancellationToken))
#pragma warning restore CA1031
                {
                    if (_consumerChannel.IsOpen)
                    {
                        try
                        {
                            await _consumerChannel
                                .BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken)
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
                    options.QueueName,
                    false,
                    options.ConsumerTag ?? string.Empty,
                    false,
                    options.Exclusive,
                    null,
                    consumer,
                    cancellationToken)
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
            _activeQueueName = null;
            SignalStopped();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default)
    {
        await _stoppedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
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
            (message, token) =>
            {
                var transportMessage = ToTransportMessage(message);
                return TransportConsumerHandlerInvoker.InvokeAsync(transportMessage, handler, token);
            },
            cancellationToken);
    }

    /// <summary>
    ///     Maps an AMQP delivery to the transport-neutral message model.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The transport message passed to generic consumer handlers.</returns>
    private TransportMessage ToTransportMessage(AmqpReceivedMessage message)
    {
        return new TransportMessage
        {
            MessagingSystem = TransportMessagingSystems.RabbitMq,
            Body = message.Body,
            Headers = message.Headers,
            Destination = _activeQueueName ?? message.Exchange,
            Route = message.RoutingKey,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            Redelivered = message.Redelivered,
            AckAsync = message.AcceptAsync,
            NackAsync = (requeue, token) => message.NackDelegate(false, requeue, token)
        };
    }

    /// <summary>
    ///     Creates a new task source used to observe consumer shutdown.
    /// </summary>
    /// <returns>The task source for the current consume session.</returns>
    private static TaskCompletionSource CreateStoppedTaskSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    ///     Marks the current consume session as stopped.
    /// </summary>
    /// <param name="exception">The broker failure that stopped the session, when one was supplied.</param>
    private void SignalStopped(Exception? exception = null)
    {
        if (exception is not null)
        {
            _stoppedTcs.TrySetException(exception);
            return;
        }

        _stoppedTcs.TrySetResult();
    }

    /// <summary>
    ///     Handles broker-initiated channel shutdown so callers can restart the consumer.
    /// </summary>
    /// <param name="sender">The channel that shut down.</param>
    /// <param name="args">The shutdown details supplied by the broker.</param>
    /// <returns>A completed task.</returns>
    private Task OnChannelShutdownAsync(object? sender, ShutdownEventArgs args)
    {
        SignalStopped(args.Exception);
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
            Body = delivery.Body.ToArray(),
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
    private static Dictionary<string, object?> CopyHeaders(IDictionary<string, object?>? headers)
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
