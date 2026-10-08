using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Publishes messages to Azure Service Bus queues or topics.
/// </summary>
public sealed class AzureServiceBusPublisher : ITransportPublisher, IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets the registry that scopes publish resilience by destination.
    /// </summary>
    private readonly ITransportCircuitBreakerRegistry _circuitBreakerRegistry;

    /// <summary>
    ///     Gets the shared Service Bus client used to create senders.
    /// </summary>
    private readonly ServiceBusClient _client;

    /// <summary>
    ///     Gets the senders cached per destination name.
    /// </summary>
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Tracks whether disposal has started.
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusPublisher" /> class.
    /// </summary>
    /// <param name="client">The shared Service Bus client used to create senders.</param>
    /// <param name="circuitBreakerRegistry">The registry that scopes publish resilience by destination.</param>
    public AzureServiceBusPublisher(
        ServiceBusClient client,
        ITransportCircuitBreakerRegistry circuitBreakerRegistry)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(circuitBreakerRegistry);
        _client = client;
        _circuitBreakerRegistry = circuitBreakerRegistry;
    }

    /// <summary>
    ///     Releases cached senders using the synchronous disposal path required by dependency injection scopes.
    /// </summary>
    /// <remarks>
    ///     Dispose runs on dispatch-scope teardown, so a sender close failure or timeout must not fail the scope.
    ///     The asynchronous close is bounded with a timeout and close errors are not propagated.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            // Bounded teardown: a slow broker close must not block scope disposal.
        }
        catch (ServiceBusException)
        {
            // A sender close failure must not fail the dispatch scope.
        }
        catch (ObjectDisposedException)
        {
            // A prior disposal already released the cached senders.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        _senders.Clear();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="ServiceBusException" /> is handled explicitly so broker failures increment the destination
    ///     circuit. Unexpected application failures are traced and rethrown without changing broker resilience state.
    /// </remarks>
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.ServiceBus,
            Destination = request.Destination,
            Route = request.Route,
            MessageId = request.MessageId,
            CorrelationId = request.CorrelationId
        });

        var circuitBreaker = _circuitBreakerRegistry.GetPublisherCircuit(request.Destination);
        TransportCircuitBreakerPermit permit;

        try
        {
            permit = circuitBreaker.AcquirePermit();
        }
        catch (TransportCircuitBreakerOpenException exception)
        {
            TransportTracing.RecordException(activity, exception);
            throw;
        }

        try
        {
            var sender = _senders.GetOrAdd(
                request.Destination,
                static (destination, client) => client.CreateSender(destination),
                _client);

            var message = AzureServiceBusMessageMapper.ToServiceBusMessage(request);

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            circuitBreaker.RecordSuccess(permit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            circuitBreaker.ReleasePermit(permit);
            throw;
        }
        catch (ServiceBusException exception)
        {
            TransportTracing.RecordException(activity, exception);
            circuitBreaker.RecordFailure(permit);
            throw;
        }
#pragma warning disable CA1031 // Last-resort publish boundary traces unexpected failures before rethrowing.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TransportTracing.RecordException(activity, exception);
            circuitBreaker.ReleasePermit(permit);
            throw;
        }
    }
}
