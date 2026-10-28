using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Consumes Kafka records with manual offset commit after handler acknowledgement.
/// </summary>
/// <remarks>
///     Each instance owns its <see cref="IConsumer{TKey,TValue}" />: the client holds single-subscription state and
///     is not thread-safe, so a client must never be shared across consumers. <see cref="StopAsync" /> closes the
///     owned client for a prompt group leave, and <see cref="DisposeAsync" /> disposes it.
///     <see cref="TransportMessage.ReturnToQueueAsync" /> seeks to the failed offset so the record is consumed again
///     before the offset is committed. <see cref="TransportMessage.AcceptAsync" /> commits the offset. Processors must
///     treat Kafka deliveries as at-least-once and make handlers idempotent.
/// </remarks>
public sealed class KafkaConsumer : IMessageConsumer
{
    /// <summary>
    ///     Gets the owned Kafka consumer used to read topic records.
    /// </summary>
    private readonly IConsumer<string, byte[]> _consumer;

    /// <summary>
    ///     Serializes start and stop operations on the consume loop.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    ///     Tracks whether asynchronous disposal has started.
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     Tracks repeated seek failures and computes consume delays between retries.
    /// </summary>
    private readonly KafkaSeekBackoff _seekBackoff;

    /// <summary>
    ///     Gets the cancellation source used to stop the active consume loop.
    /// </summary>
    private CancellationTokenSource? _consumeCts;

    /// <summary>
    ///     Gets the background task running the consume loop.
    /// </summary>
    private Task? _consumeTask;

    /// <summary>
    ///     Gets the delay to apply before the next consume call after a seek retry.
    /// </summary>
    private TimeSpan _pendingConsumeDelay;

    /// <summary>
    ///     Signals when the active consume loop stops because of shutdown or cancellation.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     The maximum time one <see cref="IConsumer{TKey,TValue}.Consume(TimeSpan)" /> call may block while waiting
    ///     for records or shutdown.
    /// </summary>
    private static readonly TimeSpan ConsumePollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     The delay before polling again after a transient consume error that leaves the client usable.
    /// </summary>
    private static readonly TimeSpan ConsumeErrorBackoff = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaConsumer" /> class.
    /// </summary>
    /// <param name="consumer">The Kafka consumer used to read topic records. Ownership transfers to this instance.</param>
    /// <param name="options">The connection settings controlling seek backoff behavior.</param>
    public KafkaConsumer(IConsumer<string, byte[]> consumer, KafkaTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(options);
        _consumer = consumer;
        _seekBackoff = new KafkaSeekBackoff(options);
    }

    /// <inheritdoc />
    public async Task StartAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInFlightMessages, 1);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumeTask is not null)
            {
                throw new InvalidOperationException("The Kafka consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var consumeToken = _consumeCts.Token;

            // Consume blocks the calling thread, so the loop runs on the thread pool; started inline, StartAsync would
            // not return until the first record arrived.
            _consumeTask = Task.Run(() => RunConsumeLoopAsync(options, handler, consumeToken), CancellationToken.None);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    ///     The maximum time to wait for the consume loop to exit after cancellation is requested.
    /// </summary>
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The maximum time to wait for a blocking <see cref="IConsumer{TKey,TValue}.Close" /> call during teardown.
    /// </summary>
    private static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    /// <remarks>
    ///     A faulted consume loop is captured into the stop signal instead of thrown here, so the terminal failure
    ///     surfaces through <see cref="WaitUntilStoppedAsync" />. The owned client is closed for a prompt group leave
    ///     once the loop exits.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumeCts is null)
            {
                SignalStopped();
                return;
            }

            var consumeCts = _consumeCts;

            try
            {
                await consumeCts.CancelAsync().ConfigureAwait(false);

                if (_consumeTask is not null)
                {
                    var closedToUnblock = false;

                    try
                    {
                        await _consumeTask.WaitAsync(StopWaitTimeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        await CloseConsumerAsync().ConfigureAwait(false);
                        closedToUnblock = true;

                        try
                        {
                            await _consumeTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            // The consume loop may still be blocked in native code.
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when cancellation stops the consume loop.
                        }
#pragma warning disable CA1031 // A faulted loop surfaces through WaitUntilStoppedAsync, not StopAsync.
                        catch (Exception exception)
#pragma warning restore CA1031
                        {
                            SignalStopped(exception);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Expected when host shutdown cancels the stop wait.
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation stops the consume loop.
                    }
#pragma warning disable CA1031 // A faulted loop surfaces through WaitUntilStoppedAsync, not StopAsync.
                    catch (Exception exception)
#pragma warning restore CA1031
                    {
                        SignalStopped(exception);
                    }

                    if (!closedToUnblock && !cancellationToken.IsCancellationRequested)
                    {
                        await CloseConsumerAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                consumeCts.Dispose();
                _consumeCts = null;
                _consumeTask = null;
                SignalStopped();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default)
    {
        if (_consumeTask is { } consumeTask)
        {
            await consumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _stoppedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _consumer.Dispose();
            _lifecycleGate.Dispose();
        }
    }

    /// <summary>
    ///     Reads records from the subscribed topic until cancellation is requested.
    /// </summary>
    /// <remarks>
    ///     Transient consume errors leave the client usable, so the loop backs off briefly and keeps polling; only a
    ///     fatal error faults the loop. A requeue nack retries a transient seek failure with the seek backoff schedule
    ///     until the seek succeeds or shutdown cancels the retry.
    /// </remarks>
    /// <param name="options">The consumer options for the active subscription.</param>
    /// <param name="handler">The handler invoked for each record.</param>
    /// <param name="cancellationToken">The token used to cancel the consume loop.</param>
    /// <returns>A task that completes when the consume loop stops.</returns>
    private async Task RunConsumeLoopAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var boundedHandler = TransportConsumerHandlerInvoker.CreateBoundedHandler(
            handler,
            options.MaxInFlightMessages);

        try
        {
            _consumer.Subscribe(options.Destination);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_pendingConsumeDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_pendingConsumeDelay, cancellationToken).ConfigureAwait(false);
                    _pendingConsumeDelay = TimeSpan.Zero;
                }

                ConsumeResult<string, byte[]>? result;

                try
                {
                    result = _consumer.Consume(ConsumePollInterval);
                }
                catch (ConsumeException exception) when (exception.Error is not null && !exception.Error.IsFatal)
                {
                    // The client recovers from a transient broker error by itself; faulting here would stall
                    // raw-host consumers until manual intervention.
                    await Task.Delay(ConsumeErrorBackoff, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (result is null)
                {
                    continue;
                }

                var offset = result.TopicPartitionOffset;

                var ackHandlers = new TransportConsumerAckHandlers
                {
                    AckAsync = _ =>
                    {
                        _seekBackoff.RecordCommit(offset);
                        CommitOffset(result);
                        return Task.CompletedTask;
                    },
                    NackAsync = async (requeue, token) =>
                    {
                        if (requeue)
                        {
                            await SeekWithRetryAsync(offset, token).ConfigureAwait(false);
                        }
                        else
                        {
                            _seekBackoff.RecordCommit(offset);
                            CommitOffset(result);
                        }
                    }
                };

                var transportMessage = KafkaMessageMapper.ToTransportMessage(
                    result,
                    options.Destination,
                    ackHandlers,
                    _seekBackoff.IsRedelivery(offset));

                await boundedHandler(transportMessage, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during consumer shutdown.
        }
#pragma warning disable CA1031 // Terminal loop failures surface through WaitUntilStoppedAsync; the signal preserves the fault before rethrow.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SignalStopped(exception);
            throw;
        }
        finally
        {
            SignalStopped();
        }
    }

    /// <summary>
    ///     Commits the offset for one consumed record.
    /// </summary>
    /// <param name="result">The consumed record whose offset should be committed.</param>
    private void CommitOffset(ConsumeResult<string, byte[]> result)
    {
        _consumer.Commit(result);
    }

    /// <summary>
    ///     Seeks to the failed offset, retrying transient seek failures with backoff until success or cancellation.
    /// </summary>
    /// <remarks>
    ///     Seek throws <see cref="KafkaException" /> when the assigned partition was revoked during a rebalance. A
    ///     fatal error is rethrown so the loop faults; a transient failure delays with the seek backoff schedule and
    ///     retries, so a rebalance coinciding with a handler failure redelivers instead of killing the consumer.
    /// </remarks>
    /// <param name="offset">The offset to seek back to.</param>
    /// <param name="cancellationToken">The token used to cancel seek retries.</param>
    /// <returns>A task that completes when the seek succeeds.</returns>
    private async Task SeekWithRetryAsync(TopicPartitionOffset offset, CancellationToken cancellationToken)
    {
        TimeSpan? lastRetryDelay = null;

        while (true)
        {
            try
            {
                _consumer.Seek(offset);
                break;
            }
            catch (KafkaException exception) when (!IsFatalKafkaError(exception))
            {
                lastRetryDelay = _seekBackoff.RecordSeek(offset);
                await Task.Delay(lastRetryDelay.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        _pendingConsumeDelay = lastRetryDelay ?? _seekBackoff.RecordSeek(offset);
    }

    /// <summary>
    ///     Returns whether a Kafka client error is fatal and the client cannot recover from it.
    /// </summary>
    /// <param name="exception">The Kafka client exception to inspect.</param>
    /// <returns><see langword="true" /> when the error is fatal; otherwise, <see langword="false" />.</returns>
    private static bool IsFatalKafkaError(KafkaException exception)
    {
        return exception.Error is not null && exception.Error.IsFatal;
    }

    /// <summary>
    ///     Closes the owned client without failing teardown when the close cannot complete.
    /// </summary>
    /// <returns>A task that completes when the close attempt finishes.</returns>
    private async Task CloseConsumerAsync()
    {
        try
        {
            await Task.Run(() => _consumer.Close(), CancellationToken.None)
                .WaitAsync(CloseWaitTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Close is best-effort when the consume loop does not exit promptly.
        }
        catch (KafkaException)
        {
            // Close is best-effort; a failed group leave must not fail host shutdown.
        }
        catch (ObjectDisposedException)
        {
            // The owned client was already released; there is nothing left to close.
        }
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
}
