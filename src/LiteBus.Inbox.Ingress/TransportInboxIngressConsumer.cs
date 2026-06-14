using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Consumes transport messages and accepts them into the inbox store as LiteBus background service work.
/// </summary>
public sealed class TransportInboxIngressConsumer : IBackgroundService
{
    /// <summary>
    ///     The buffered deliveries waiting for a batch accept flush.
    /// </summary>
    private readonly List<TransportMessage> _batchBuffer = [];

    /// <summary>
    ///     The lock that serializes access to the optional batch accept buffer.
    /// </summary>
    private readonly object _batchSync = new();

    /// <summary>
    ///     Gets the optional circuit breaker shared with the transport connection manager.
    /// </summary>
    private readonly ITransportCircuitBreaker? _circuitBreaker;

    /// <summary>
    ///     Gets the transport consumer used to subscribe to the ingress destination.
    /// </summary>
    private readonly IMessageConsumer _consumer;

    /// <summary>
    ///     Gets the handler that maps deliveries to inbox acceptance through <see cref="TransportInboxIngressHandler" />.
    /// </summary>
    private readonly TransportInboxIngressHandler _handler;

    /// <summary>
    ///     Gets the hosting options that control whether the ingress loop is enabled.
    /// </summary>
    private readonly TransportInboxIngressHostOptions _hostOptions;

    /// <summary>
    ///     Gets the logger used for ingress restart diagnostics.
    /// </summary>
    private readonly ILogger<TransportInboxIngressConsumer> _logger;

    /// <summary>
    ///     Gets the ingress destination and consumer settings.
    /// </summary>
    private readonly TransportInboxIngressOptions _options;

    /// <summary>
    ///     Limits buffered deliveries to <see cref="TransportInboxIngressOptions.PrefetchCount" /> while a flush is in
    ///     progress.
    /// </summary>
    private SemaphoreSlim? _batchAdmission;

    /// <summary>
    ///     The timer that flushes partial batches after <see cref="TransportInboxIngressOptions.BatchMaxWait" />.
    /// </summary>
    private Timer? _batchFlushTimer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportInboxIngressConsumer" /> class.
    /// </summary>
    /// <param name="consumer">The transport consumer used to subscribe to the ingress destination.</param>
    /// <param name="handler">The handler that maps deliveries to inbox acceptance.</param>
    /// <param name="options">The ingress destination and consumer settings.</param>
    /// <param name="hostOptions">The hosting options that control whether the ingress loop is enabled.</param>
    /// <param name="circuitBreaker">The optional circuit breaker shared with the transport connection manager.</param>
    /// <param name="logger">The optional logger for ingress restart diagnostics.</param>
    public TransportInboxIngressConsumer(
        IMessageConsumer consumer,
        TransportInboxIngressHandler handler,
        TransportInboxIngressOptions options,
        TransportInboxIngressHostOptions hostOptions,
        ITransportCircuitBreaker? circuitBreaker = null,
        ILogger<TransportInboxIngressConsumer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        _consumer = consumer;
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        ArgumentNullException.ThrowIfNull(hostOptions);
        _hostOptions = hostOptions;
        _circuitBreaker = circuitBreaker;
        _logger = logger ?? NullLogger<TransportInboxIngressConsumer>.Instance;
    }

    /// <summary>
    ///     The delegate that writes the delivery discard event after a non-requeue acceptance failure.
    /// </summary>
    private static readonly Action<ILogger, string?, string?, string?, Exception> DeliveryDiscardedAfterAcceptFailureMessage =
        LoggerMessage.Define<string?, string?, string?>(
            LogLevel.Warning,
            new EventId(3005, "DeliveryDiscardedAfterAcceptFailure"),
            "Transport inbox ingress delivery discarded after non-requeue acceptance failure. MessageId={MessageId}, Destination={Destination}, CorrelationId={CorrelationId}");

    /// <summary>
    ///     The delegate that writes the batch accept fallback event.
    /// </summary>
    private static readonly Action<ILogger, int, Exception> BatchAcceptFallbackMessage =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(3006, "BatchAcceptFallback"),
            "Transport inbox ingress batch accept failed; falling back to per-message acceptance for {BatchSize} deliveries.");

    /// <inheritdoc />
    /// <remarks>
    ///     The ingress restart loop uses a broad <see cref="Exception" /> handler because broker SDK faults cannot be
    ///     enumerated safely at the transport boundary. Failures are logged and the consumer loop retries after
    ///     <see cref="TransportInboxIngressHostOptions.RetryPollInterval" />.
    /// </remarks>
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hostOptions.Enabled)
        {
            return;
        }

        var consumerOptions = new TransportConsumerOptions
        {
            Destination = _options.Destination,
            PrefetchCount = _options.PrefetchCount,
            DeclareDestination = _options.DeclareDestination,
            DurableDestination = _options.DurableDestination
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _circuitBreaker?.ThrowIfOpen();
                await _consumer.StartAsync(consumerOptions, HandleDeliveryAsync, stoppingToken).ConfigureAwait(false);

                using var stopRegistration = stoppingToken.Register(static state =>
                {
                    var consumer = (IMessageConsumer)state!;
                    _ = consumer.StopAsync(CancellationToken.None);
                }, _consumer);

                await _consumer.WaitUntilStoppedAsync(stoppingToken).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

#pragma warning disable CA1031 // Ingress restart boundary records broker faults and retries the consumer loop.
            catch (Exception exception)
            {
                _circuitBreaker?.RecordFailure();
                TransportInboxIngressLogMessages.IngressRestarting(_logger, exception);
            }
#pragma warning restore CA1031
            finally
            {
                CancelBatchFlushTimer();
                await FlushBatchBufferAsync(CancellationToken.None).ConfigureAwait(false);
                await _consumer.StopAsync(stoppingToken).ConfigureAwait(false);
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
        await WaitForBatchAdmissionAsync(cancellationToken).ConfigureAwait(false);

        List<TransportMessage>? batchToFlush = null;
        var admissionReleased = false;

        try
        {
            lock (_batchSync)
            {
                var shouldScheduleFlush = _batchBuffer.Count == 0 && _options.BatchMaxWait > TimeSpan.Zero;
                _batchBuffer.Add(message);

                if (_batchBuffer.Count >= GetBatchBufferCapacity())
                {
                    batchToFlush = [.. _batchBuffer];
                    _batchBuffer.Clear();
                    CancelBatchFlushTimerUnsafe();
                }
                else if (shouldScheduleFlush)
                {
                    ScheduleBatchFlushTimerUnsafe();
                }
            }

            if (batchToFlush is not null)
            {
                await AcceptAndAcknowledgeBatchAsync(batchToFlush, cancellationToken).ConfigureAwait(false);
                admissionReleased = true;
            }
        }
        catch
        {
            if (!admissionReleased)
            {
                ReleaseBatchAdmission();
            }

            throw;
        }
    }

    /// <summary>
    ///     Schedules a timer flush for the current partial batch buffer.
    /// </summary>
    private void ScheduleBatchFlushTimerUnsafe()
    {
        CancelBatchFlushTimerUnsafe();

        _batchFlushTimer = new Timer(
            static state => _ = ((TransportInboxIngressConsumer) state!).OnBatchFlushTimerElapsedAsync(),
            this,
            _options.BatchMaxWait,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     Flushes the batch buffer when the max-wait timer elapses.
    /// </summary>
    /// <returns>A task that completes when the partial batch has been flushed.</returns>
    /// <remarks>
    ///     Batch flush timer failures use a broad <see cref="Exception" /> handler because store and broker faults
    ///     cannot be enumerated safely at the ingress boundary. Failures are logged so partial batches are visible
    ///     without stopping the ingress loop.
    /// </remarks>
    private async Task OnBatchFlushTimerElapsedAsync()
    {
        try
        {
            await FlushBatchBufferAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Batch flush timer failures can originate from any store or broker fault.
        catch (Exception exception)
        {
            TransportInboxIngressLogMessages.BatchFlushFailed(_logger, exception);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Cancels the active batch flush timer when the consumer stops or the buffer is flushed by size.
    /// </summary>
    private void CancelBatchFlushTimer()
    {
        lock (_batchSync)
        {
            CancelBatchFlushTimerUnsafe();
        }
    }

    /// <summary>
    ///     Cancels the active batch flush timer without acquiring the batch lock.
    /// </summary>
    private void CancelBatchFlushTimerUnsafe()
    {
        _batchFlushTimer?.Dispose();
        _batchFlushTimer = null;
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
            CancelBatchFlushTimerUnsafe();
        }

        await AcceptAndAcknowledgeBatchAsync(batchToFlush, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Accepts one delivery into the inbox and acknowledges the broker delivery.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the delivery has been acknowledged.</returns>
    /// <remarks>
    ///     Non-requeue acceptance failures use a broad <see cref="Exception" /> handler because store, contract, and
    ///     serialization faults cannot be enumerated safely at the ingress boundary. Those deliveries are logged and
    ///     discarded so poison messages do not block the consume loop.
    /// </remarks>
    private async Task AcceptAndAcknowledgeAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        _circuitBreaker?.ThrowIfOpen();

        try
        {
            await _handler.AcceptAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IngressAckPolicy.ShouldRequeue(exception, _options.RequeueOnFailure))
        {
            await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

#pragma warning disable CA1031 // Non-requeue acceptance failures are discarded so poison deliveries do not block the loop.
        catch (Exception exception)
        {
            DeliveryDiscardedAfterAcceptFailureMessage(
                _logger,
                message.MessageId,
                message.Destination,
                message.CorrelationId,
                exception);
            await message.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
#pragma warning restore CA1031

        await AcknowledgeDeliveryAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Acknowledges a broker delivery after the inbox store has accepted it.
    /// </summary>
    /// <param name="message">The transport delivery whose broker acknowledgement should be sent.</param>
    /// <param name="cancellationToken">The token used to cancel the acknowledgement.</param>
    /// <returns>A task that completes when the delivery has been acknowledged or returned to the queue.</returns>
    /// <remarks>
    ///     When acknowledgement fails after a successful store accept, the delivery is returned to the queue so broker
    ///     redelivery is absorbed idempotently by the existing inbox row when
    ///     <see cref="TransportInboxIngressOptions.RequireStableIdentity" /> supplies broker-scoped identity and idempotency.
    /// </remarks>
    private async Task AcknowledgeDeliveryAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            _circuitBreaker?.RecordSuccess();
        }

#pragma warning disable CA1031 // Broker acknowledgement failures can originate from any transport SDK.
        catch (Exception exception)
        {
            _circuitBreaker?.RecordFailure();
            TransportInboxIngressTelemetry.RecordAckFailedAfterAccept();
            TransportInboxIngressLogMessages.AckFailedAfterAccept(_logger, exception);
            await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Accepts a buffered batch into the inbox and acknowledges every broker delivery on success.
    /// </summary>
    /// <param name="messages">The buffered transport deliveries.</param>
    /// <param name="cancellationToken">The token used to cancel acceptance.</param>
    /// <returns>A task that completes when the batch has been accepted and acknowledged.</returns>
    /// <remarks>
    ///     Batch accept failures use a broad <see cref="Exception" /> handler because store faults cannot be narrowed
    ///     without losing isolated poison handling. Failures are logged before per-message fallback acceptance.
    /// </remarks>
    private async Task AcceptAndAcknowledgeBatchAsync(
        IReadOnlyList<TransportMessage> messages,
        CancellationToken cancellationToken)
    {
        _circuitBreaker?.ThrowIfOpen();

        try
        {
            await _handler.AcceptBatchAsync(messages, cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable CA1031 // Batch accept failures fall back to per-message acceptance for isolated poison handling.
        catch (Exception exception)
        {
            BatchAcceptFallbackMessage(_logger, messages.Count, exception);

            foreach (var message in messages)
            {
                try
                {
                    await AcceptAndAcknowledgeAsync(message, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ReleaseBatchAdmission();
                }
            }

            return;
        }
#pragma warning restore CA1031

        foreach (var message in messages)
        {
            try
            {
                await AcknowledgeDeliveryAsync(message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ReleaseBatchAdmission();
            }
        }
    }

    /// <summary>
    ///     Gets the maximum number of deliveries that may wait in the batch buffer.
    /// </summary>
    /// <returns>The batch buffer capacity derived from prefetch settings.</returns>
    private int GetBatchBufferCapacity()
    {
        return _options.PrefetchCount > 0 ? _options.PrefetchCount : 1;
    }

    /// <summary>
    ///     Blocks until a batch admission slot is available or cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel admission.</param>
    /// <returns>A task that completes when the delivery may be buffered.</returns>
    private async Task WaitForBatchAdmissionAsync(CancellationToken cancellationToken)
    {
        _batchAdmission ??= new SemaphoreSlim(GetBatchBufferCapacity(), GetBatchBufferCapacity());
        await _batchAdmission.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Releases one batch admission slot after a buffered delivery is acknowledged or rejected.
    /// </summary>
    private void ReleaseBatchAdmission()
    {
        _batchAdmission?.Release();
    }
}