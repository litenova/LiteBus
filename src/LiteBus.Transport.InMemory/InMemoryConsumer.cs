using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Consumes in-memory channel deliveries with manual acknowledgement support.
/// </summary>
/// <remarks>
///     When a handler throws, the delivery is returned to the channel for redelivery by default. Handlers that reject a
///     message without throwing should call <see cref="TransportMessage.ReturnToQueueAsync" /> or
///     <see cref="TransportMessage.DiscardAsync" /> explicitly. This differs from broker transports where the broker owns
///     retry and dead-letter policy.
/// </remarks>
public sealed class InMemoryConsumer : IMessageConsumer
{
    /// <summary>
    ///     Gets the shared broker supplying channel readers.
    /// </summary>
    private readonly InMemoryTransportBroker _broker;

    /// <summary>
    ///     Serializes start and stop operations on the consume loop.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    ///     Gets the cancellation source used to stop the active consume loop.
    /// </summary>
    private CancellationTokenSource? _consumeCts;

    /// <summary>
    ///     Gets the background task running the consume loop.
    /// </summary>
    private Task? _consumeTask;

    /// <summary>
    ///     Signals when the active consume loop stops because of shutdown or cancellation.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryConsumer" /> class.
    /// </summary>
    /// <param name="broker">The shared broker supplying channel readers.</param>
    public InMemoryConsumer(InMemoryTransportBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
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
                throw new InvalidOperationException("The in-memory consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var endpoint = _broker.GetOrCreateEndpoint(options.Destination);
            _consumeTask = RunConsumeLoopAsync(endpoint, handler, _consumeCts.Token);
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

    /// <summary>
    ///     Reads deliveries from the destination channel until cancellation is requested.
    /// </summary>
    /// <param name="endpoint">The destination endpoint supplying deliveries.</param>
    /// <param name="handler">The handler invoked for each delivery.</param>
    /// <param name="cancellationToken">The token used to cancel the consume loop.</param>
    /// <returns>A task that completes when the consume loop stops.</returns>
    private async Task RunConsumeLoopAsync(
        InMemoryDestinationEndpoint endpoint,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var reader = endpoint.Reader;

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var delivery))
                {
                    var transportMessage = CreateTransportMessage(endpoint, delivery);
                    using var activity = TransportTracing.StartConsumeActivity(transportMessage);

                    await TransportConsumerHandlerInvoker.InvokeAsync(transportMessage, handler, cancellationToken)
                        .ConfigureAwait(false);
                }
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
    ///     Creates a transport message with acknowledgement delegates for one pending delivery.
    /// </summary>
    /// <param name="endpoint">The destination endpoint used to requeue rejected deliveries.</param>
    /// <param name="delivery">The pending delivery read from the channel.</param>
    /// <returns>The transport message passed to consumer handlers.</returns>
    private static TransportMessage CreateTransportMessage(
        InMemoryDestinationEndpoint endpoint,
        InMemoryPendingDelivery delivery)
    {
        return new TransportMessage
        {
            Body = delivery.Body,
            Headers = delivery.Headers,
            Destination = delivery.Destination,
            Route = delivery.Route,
            MessageId = delivery.MessageId,
            CorrelationId = delivery.CorrelationId,
            Redelivered = delivery.Redelivered,
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (requeue, token) => RequeueIfNeededAsync(endpoint, delivery, requeue, token)
        };
    }

    /// <summary>
    ///     Requeues a rejected delivery when the handler requests redelivery.
    /// </summary>
    /// <param name="endpoint">The destination endpoint receiving the requeued delivery.</param>
    /// <param name="delivery">The rejected delivery.</param>
    /// <param name="requeue">A value indicating whether the delivery should be returned to the channel.</param>
    /// <param name="cancellationToken">The token used to cancel the requeue operation.</param>
    /// <returns>A task that completes when the rejection has been processed.</returns>
    private static async Task RequeueIfNeededAsync(
        InMemoryDestinationEndpoint endpoint,
        InMemoryPendingDelivery delivery,
        bool requeue,
        CancellationToken cancellationToken)
    {
        if (!requeue)
        {
            return;
        }

        var redelivered = new InMemoryPendingDelivery
        {
            Body = delivery.Body,
            Headers = delivery.Headers,
            Destination = delivery.Destination,
            Route = delivery.Route,
            MessageId = delivery.MessageId,
            CorrelationId = delivery.CorrelationId,
            Redelivered = true
        };

        await endpoint.Writer.WriteAsync(redelivered, cancellationToken).ConfigureAwait(false);
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
    private void SignalStopped()
    {
        _stoppedTcs.TrySetResult();
    }
}