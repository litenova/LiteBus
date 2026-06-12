using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Consumes Azure Service Bus deliveries with manual completion support and structured recovery on processor errors.
/// </summary>
public sealed class AzureServiceBusConsumer : IMessageConsumer
{
    /// <summary>
    ///     Gets the shared Service Bus client used to create processors.
    /// </summary>
    private readonly ServiceBusClient _client;

    /// <summary>
    ///     Serializes start and stop operations on the processor.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    ///     Gets the transport options controlling reconnect backoff.
    /// </summary>
    private readonly AzureServiceBusTransportOptions _options;

    /// <summary>
    ///     Gets the cancellation source used to stop the active recovery loop.
    /// </summary>
    private CancellationTokenSource? _consumeCts;

    /// <summary>
    ///     Gets the background task running the processor recovery loop.
    /// </summary>
    private Task? _consumeTask;

    /// <summary>
    ///     Signals when the active processor stops because of shutdown or failure.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusConsumer" /> class.
    /// </summary>
    /// <param name="client">The shared Service Bus client used to create processors.</param>
    /// <param name="options">The transport options controlling reconnect backoff.</param>
    public AzureServiceBusConsumer(ServiceBusClient client, AzureServiceBusTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
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
                throw new InvalidOperationException("The Azure Service Bus consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _consumeTask = RunWithRecoveryAsync(options, handler, _consumeCts.Token);
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
                    // Expected when cancellation stops the recovery loop.
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
    ///     Runs the Service Bus processor until cancellation, restarting with exponential backoff after recoverable errors.
    /// </summary>
    /// <remarks>
    ///     <see cref="ServiceBusException" /> and <see cref="ObjectDisposedException" /> are handled explicitly before
    ///     the final <see cref="Exception" /> handler, which retries on any other unexpected processor failure.
    /// </remarks>
    /// <param name="options">The consumer options for the active subscription.</param>
    /// <param name="handler">The handler invoked for each delivery.</param>
    /// <param name="cancellationToken">The token used to cancel the recovery loop.</param>
    /// <returns>A task that completes when the recovery loop stops.</returns>
    private async Task RunWithRecoveryAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var retryDelay = _options.ConsumerErrorRetryInterval;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunProcessorSessionAsync(options, handler, cancellationToken).ConfigureAwait(false);
                    retryDelay = _options.ConsumerErrorRetryInterval;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ServiceBusException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);

                    retryDelay = TimeSpan.FromMilliseconds(
                        Math.Min(retryDelay.TotalMilliseconds * 2, _options.ConsumerErrorRetryMaxInterval.TotalMilliseconds));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
#pragma warning disable CA1031 // Last-resort recovery boundary: unexpected processor failures restart the session with backoff.
                catch (Exception)
#pragma warning restore CA1031
                {
                    // Last-resort recovery boundary: unexpected processor failures restart the session with backoff.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);

                    retryDelay = TimeSpan.FromMilliseconds(
                        Math.Min(retryDelay.TotalMilliseconds * 2, _options.ConsumerErrorRetryMaxInterval.TotalMilliseconds));
                }
            }
        }
        finally
        {
            SignalStopped();
        }
    }

    /// <summary>
    ///     Starts one processor session and waits until it stops because of an error or cancellation.
    /// </summary>
    /// <param name="options">The consumer options for the active subscription.</param>
    /// <param name="handler">The handler invoked for each delivery.</param>
    /// <param name="cancellationToken">The token used to cancel the session.</param>
    /// <returns>A task that completes when the processor session ends.</returns>
    private async Task RunProcessorSessionAsync(
        TransportConsumerOptions options,
        Func<TransportMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var sessionStopped = CreateStoppedTaskSource();

        var processor = _client.CreateProcessor(
            options.Destination,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = options.PrefetchCount > 0 ? options.PrefetchCount : 1,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        try
        {
            processor.ProcessMessageAsync += async args =>
            {
                var ackHandlers = new TransportConsumerAckHandlers
                {
                    AckAsync = token => args.CompleteMessageAsync(args.Message, token),
                    NackAsync = (requeue, token) => requeue
                        ? args.AbandonMessageAsync(args.Message, cancellationToken: token)
                        : args.DeadLetterMessageAsync(args.Message, cancellationToken: token)
                };

                var transportMessage = AzureServiceBusMessageMapper.ToTransportMessage(
                    args.Message,
                    options.Destination,
                    ackHandlers);

                using var activity = TransportTracing.StartConsumeActivity(transportMessage);

                await TransportConsumerHandlerInvoker.InvokeAsync(transportMessage, handler, cancellationToken)
                    .ConfigureAwait(false);
            };

            processor.ProcessErrorAsync += _ =>
            {
                sessionStopped.TrySetResult();
                return Task.CompletedTask;
            };

            await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
            await sessionStopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await processor.StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await processor.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a new task source used to observe processor shutdown.
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