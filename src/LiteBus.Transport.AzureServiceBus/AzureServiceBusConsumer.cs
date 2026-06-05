using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Consumes Azure Service Bus deliveries with manual completion support.
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
    ///     Signals when the active processor stops because of shutdown or failure.
    /// </summary>
    private TaskCompletionSource _stoppedTcs = CreateStoppedTaskSource();

    /// <summary>
    ///     Gets the active Service Bus processor, if the consume loop has started.
    /// </summary>
    private ServiceBusProcessor? _processor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusConsumer" /> class.
    /// </summary>
    /// <param name="client">The shared Service Bus client used to create processors.</param>
    public AzureServiceBusConsumer(ServiceBusClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
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
            if (_processor is not null)
            {
                throw new InvalidOperationException("The Azure Service Bus consumer is already started.");
            }

            _stoppedTcs = CreateStoppedTaskSource();
            _processor = _client.CreateProcessor(
                options.Destination,
                new ServiceBusProcessorOptions
                {
                    AutoCompleteMessages = false,
                    MaxConcurrentCalls = options.PrefetchCount > 0 ? options.PrefetchCount : 1,
                    ReceiveMode = ServiceBusReceiveMode.PeekLock
                });

            _processor.ProcessMessageAsync += async args =>
            {
                var transportMessage = AzureServiceBusMessageMapper.ToTransportMessage(
                    args.Message,
                    options.Destination,
                    token => args.CompleteMessageAsync(args.Message, token),
                    token => args.AbandonMessageAsync(args.Message, cancellationToken: token),
                    token => args.DeadLetterMessageAsync(args.Message, cancellationToken: token));

                await handler(transportMessage, cancellationToken).ConfigureAwait(false);
            };

            _processor.ProcessErrorAsync += _ =>
            {
                SignalStopped();
                return Task.CompletedTask;
            };

            await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
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
            if (_processor is null)
            {
                SignalStopped();
                return;
            }

            await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
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
    ///     Creates a new task source used to observe processor shutdown.
    /// </summary>
    /// <returns>The task source for the current consume session.</returns>
    private static TaskCompletionSource CreateStoppedTaskSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Marks the current consume session as stopped.
    /// </summary>
    private void SignalStopped() => _stoppedTcs.TrySetResult();
}

