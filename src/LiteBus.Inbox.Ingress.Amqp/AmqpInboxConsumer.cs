using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Ingress.Transport;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using LiteBus.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Consumes transport messages and accepts them into the inbox store as LiteBus background service work.
/// </summary>
public sealed class AmqpInboxConsumer : IBackgroundService
{
    /// <summary>
    ///     Gets the transport consumer used to subscribe to the ingress queue.
    /// </summary>
    private readonly IMessageConsumer _consumer;

    /// <summary>
    ///     Gets the optional circuit breaker shared with the transport connection manager.
    /// </summary>
    private readonly ITransportCircuitBreaker? _circuitBreaker;

    /// <summary>
    ///     Gets the handler that maps deliveries to <see cref="Abstractions.IInbox.AcceptAsync" />.
    /// </summary>
    private readonly TransportInboxIngressHandler _handler;

    /// <summary>
    ///     Gets the hosting options that control whether the ingress loop is enabled.
    /// </summary>
    private readonly AmqpInboxIngressHostOptions _hostOptions;

    /// <summary>
    ///     Gets the ingress queue and broker settings.
    /// </summary>
    private readonly AmqpInboxIngressOptions _options;

    /// <summary>
    ///     Gets the logger used for ingress restart diagnostics.
    /// </summary>
    private readonly ILogger<AmqpInboxConsumer> _logger;

    /// <summary>
    ///     The lock that serializes access to the optional batch accept buffer.
    /// </summary>
    private readonly object _batchSync = new();

    /// <summary>
    ///     The buffered deliveries waiting for a batch accept flush.
    /// </summary>
    private readonly List<TransportMessage> _batchBuffer = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxConsumer" /> class.
    /// </summary>
    /// <param name="consumer">The transport consumer used to subscribe to the ingress queue.</param>
    /// <param name="handler">The handler that maps deliveries to inbox acceptance.</param>
    /// <param name="options">The ingress queue and broker settings.</param>
    /// <param name="hostOptions">The hosting options that control whether the ingress loop is enabled.</param>
    /// <param name="connectionManager">The optional connection manager used to resolve the shared circuit breaker.</param>
    /// <param name="logger">The optional logger for ingress restart diagnostics.</param>
    public AmqpInboxConsumer(
        IMessageConsumer consumer,
        TransportInboxIngressHandler handler,
        AmqpInboxIngressOptions options,
        AmqpInboxIngressHostOptions hostOptions,
        IAmqpConnectionManager? connectionManager = null,
        ILogger<AmqpInboxConsumer>? logger = null)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _circuitBreaker = connectionManager is AmqpConnectionManager manager ? manager.TransportCircuitBreaker : null;
        _logger = logger ?? NullLogger<AmqpInboxConsumer>.Instance;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hostOptions.Enabled)
        {
            return;
        }

        var consumerOptions = new TransportConsumerOptions
        {
            Destination = _options.QueueName,
            PrefetchCount = _options.PrefetchCount,
            DeclareDestination = _options.DeclareQueue,
            DurableDestination = _options.DurableQueue
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _circuitBreaker?.ThrowIfOpen();
                await _consumer.StartAsync(consumerOptions, HandleDeliveryAsync, stoppingToken).ConfigureAwait(false);
                await _consumer.WaitUntilStoppedAsync(stoppingToken).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _circuitBreaker?.RecordFailure();
                _logger.LogError(exception, "AMQP inbox ingress consumer stopped unexpectedly; retrying after the poll interval.");
            }
            finally
            {
                await FlushBatchBufferAsync(CancellationToken.None).ConfigureAwait(false);
                await _consumer.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (_hostOptions.RetryPollInterval > TimeSpan.Zero)
            {
                await Task.Delay(_hostOptions.RetryPollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Accepts one transport delivery into the inbox and acknowledges the broker delivery.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery has been acknowledged.</returns>
    private async Task HandleDeliveryAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        if (_options.EnableBatchAccept)
        {
            await HandleDeliveryWithBatchBufferAsync(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AcceptAndAcknowledgeAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Buffers a delivery until the prefetch threshold is reached, then accepts and acknowledges as a batch.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery is buffered or flushed.</returns>
    private async Task HandleDeliveryWithBatchBufferAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        List<TransportMessage>? batchToFlush = null;

        lock (_batchSync)
        {
            _batchBuffer.Add(message);

            if (_batchBuffer.Count >= _options.PrefetchCount)
            {
                batchToFlush = [.. _batchBuffer];
                _batchBuffer.Clear();
            }
        }

        if (batchToFlush is not null)
        {
            await AcceptAndAcknowledgeBatchAsync(batchToFlush, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Flushes any buffered deliveries still waiting for a batch accept call.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the buffer is empty.</returns>
    private async Task FlushBatchBufferAsync(CancellationToken cancellationToken)
    {
        List<TransportMessage>? batchToFlush;

        lock (_batchSync)
        {
            if (_batchBuffer.Count == 0)
            {
                return;
            }

            batchToFlush = [.. _batchBuffer];
            _batchBuffer.Clear();
        }

        await AcceptAndAcknowledgeBatchAsync(batchToFlush, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Accepts one delivery into the inbox and acknowledges the broker delivery.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery has been acknowledged.</returns>
    private async Task AcceptAndAcknowledgeAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        _circuitBreaker?.ThrowIfOpen();

        try
        {
            await _handler.AcceptAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldRequeue(exception))
        {
            await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception)
        {
            await message.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            _circuitBreaker?.RecordSuccess();
        }
        catch
        {
            _circuitBreaker?.RecordFailure();
            await message.DiscardAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Accepts a buffered batch into the inbox and acknowledges every broker delivery on success.
    /// </summary>
    /// <param name="messages">The buffered transport deliveries.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the batch has been accepted and acknowledged.</returns>
    private async Task AcceptAndAcknowledgeBatchAsync(
        IReadOnlyList<TransportMessage> messages,
        CancellationToken cancellationToken)
    {
        _circuitBreaker?.ThrowIfOpen();

        try
        {
            await _handler.AcceptBatchAsync(messages, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldRequeue(exception))
        {
            foreach (var message in messages)
            {
                await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }
        catch (Exception)
        {
            foreach (var message in messages)
            {
                await message.DiscardAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var message in messages)
        {
            try
            {
                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
            }
            catch
            {
                _circuitBreaker?.RecordFailure();
                await message.DiscardAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Determines whether a failed delivery should be requeued for retry.
    /// </summary>
    /// <param name="exception">The exception thrown while accepting the delivery.</param>
    /// <returns><see langword="true" /> when the broker should requeue the message; otherwise <see langword="false" />.</returns>
    private bool ShouldRequeue(Exception exception)
    {
        if (!_options.RequeueOnFailure)
        {
            return false;
        }

        exception = UnwrapException(exception);

        return exception is not (
            MessageContractNotRegisteredException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or System.Text.Json.JsonException);
    }

    /// <summary>
    ///     Unwraps reflection and aggregate wrappers so acknowledgement policy inspects the root failure.
    /// </summary>
    /// <param name="exception">The exception observed by the consumer.</param>
    /// <returns>The root exception thrown by inbox acceptance.</returns>
    private static Exception UnwrapException(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case TargetInvocationException target when target.InnerException is not null:
                    exception = target.InnerException;
                    continue;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1:
                    exception = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return exception;
            }
        }
    }
}
