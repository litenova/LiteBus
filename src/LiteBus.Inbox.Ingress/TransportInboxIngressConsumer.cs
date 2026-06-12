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
    ///     Gets the handler that maps deliveries to <see cref="IInbox.AcceptAsync{TMessage}(InboxAcceptItem{TMessage}, System.Threading.CancellationToken)" />.
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
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _circuitBreaker = circuitBreaker;
        _logger = logger ?? NullLogger<TransportInboxIngressConsumer>.Instance;
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
                TransportInboxIngressLogMessages.IngressRestarting(_logger, exception);
            }
            finally
            {
                CancelBatchFlushTimer();
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
    private async Task OnBatchFlushTimerElapsedAsync()
    {
        try
        {
            await FlushBatchBufferAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TransportInboxIngressLogMessages.BatchFlushFailed(_logger, exception);
        }
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
        catch (Exception exception)
        {
            _circuitBreaker?.RecordFailure();
            TransportInboxIngressTelemetry.RecordAckFailedAfterAccept();
            TransportInboxIngressLogMessages.AckFailedAfterAccept(_logger, exception);
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
        catch (Exception exception) when (IngressAckPolicy.ShouldRequeue(exception, _options.RequeueOnFailure))
        {
            foreach (var message in messages)
            {
                await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
                ReleaseBatchAdmission();
            }

            return;
        }
        catch (Exception)
        {
            foreach (var message in messages)
            {
                await message.DiscardAsync(cancellationToken).ConfigureAwait(false);
                ReleaseBatchAdmission();
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
            catch (Exception exception)
            {
                _circuitBreaker?.RecordFailure();
                TransportInboxIngressTelemetry.RecordAckFailedAfterAccept();
                TransportInboxIngressLogMessages.AckFailedAfterAccept(_logger, exception);
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