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
///     Kafka does not provide queue-style negative acknowledgement. <see cref="TransportMessage.ReturnToQueueAsync" />
///     and <see cref="TransportMessage.DiscardAsync" /> do not change broker state until the consumer restarts or the
///     handler calls <see cref="TransportMessage.AcceptAsync" />, which commits the offset. Processors must treat Kafka
///     deliveries as at-least-once and make handlers idempotent.
/// </remarks>
public sealed class KafkaConsumer : IMessageConsumer
{
    /// <summary>
    ///     Gets the Kafka consumer used to read topic records.
    /// </summary>
    private readonly IConsumer<string, byte[]> _consumer;

    /// <summary>
    ///     Serializes start and stop operations on the consume loop.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    ///     Signals when the active consume loop stops because of shutdown or cancellation.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     Gets the cancellation source used to stop the active consume loop.
    /// </summary>
    private CancellationTokenSource? _consumeCts;

    /// <summary>
    ///     Gets the background task running the consume loop.
    /// </summary>
    private Task? _consumeTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaConsumer" /> class.
    /// </summary>
    /// <param name="consumer">The Kafka consumer used to read topic records.</param>
    public KafkaConsumer(IConsumer<string, byte[]> consumer)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
    }

    /// <inheritdoc />
    public async Task StartAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_consumeTask is not null)
            {
                throw new InvalidOperationException("The Kafka consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _consumer.Subscribe(options.Destination);
            _consumeTask = RunConsumeLoopAsync(options, handler, _consumeCts.Token);
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
            if (_consumeCts is null)
            {
                SignalStopped();
                return;
            }

            await _consumeCts.CancelAsync().ConfigureAwait(false);

            if (_consumeTask is not null)
            {
                try
                {
                    await _consumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation stops the consume loop.
                }
            }

            _consumer.Unsubscribe();
            _consumeCts.Dispose();
            _consumeCts = null;
            _consumeTask = null;
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
        _consumer.Close();
        _consumer.Dispose();
        _lifecycleGate.Dispose();
    }

    /// <summary>
    ///     Reads records from the subscribed topic until cancellation is requested.
    /// </summary>
    /// <param name="options">The consumer options for the active subscription.</param>
    /// <param name="handler">The handler invoked for each record.</param>
    /// <param name="cancellationToken">The token used to cancel the consume loop.</param>
    /// <returns>A task that completes when the consume loop stops.</returns>
    private async Task RunConsumeLoopAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]> result;

                try
                {
                    result = _consumer.Consume(cancellationToken);
                }
                catch (ConsumeException)
                {
                    SignalStopped();
                    throw;
                }

                var transportMessage = KafkaMessageMapper.ToTransportMessage(
                    result,
                    options.Destination,
                    _ =>
                    {
                        CommitOffset(result);
                        return Task.CompletedTask;
                    });

                await handler(transportMessage, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during consumer shutdown.
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
    ///     Creates a new task source used to observe consumer shutdown.
    /// </summary>
    /// <returns>The task source for the current consume session.</returns>
    private static TaskCompletionSource CreateStoppedTaskSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Marks the current consume session as stopped.
    /// </summary>
    private void SignalStopped() => _stoppedTcs.TrySetResult();
}

