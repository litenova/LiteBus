using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Publishes messages to Azure Service Bus queues or topics.
/// </summary>
public sealed class AzureServiceBusPublisher : IMessageTransport, IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets the circuit breaker guarding publish operations.
    /// </summary>
    private readonly ITransportCircuitBreaker _circuitBreaker;

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
    ///     Initializes a new instance of the <see cref="AzureServiceBusPublisher" /> class.
    /// </summary>
    /// <param name="client">The shared Service Bus client used to create senders.</param>
    /// <param name="circuitBreaker">The circuit breaker guarding publish operations.</param>
    public AzureServiceBusPublisher(ServiceBusClient client, ITransportCircuitBreaker circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        _client = client;
        _circuitBreaker = circuitBreaker;
    }

    /// <summary>
    ///     Releases cached senders using the synchronous disposal path required by dependency injection scopes.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        _senders.Clear();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="ServiceBusException" /> is handled explicitly so broker failures increment the circuit breaker.
    ///     The final <see cref="Exception" /> handler records any other non-cancellation failure before rethrowing.
    /// </remarks>
    public async Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _circuitBreaker.ThrowIfOpen();

        try
        {
            var sender = _senders.GetOrAdd(
                request.Destination,
                static (destination, client) => client.CreateSender(destination),
                _client);

            var message = AzureServiceBusMessageMapper.ToServiceBusMessage(request);

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            _circuitBreaker.RecordSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ServiceBusException)
        {
            _circuitBreaker.RecordFailure();
            throw;
        }
#pragma warning disable CA1031 // Last-resort publish boundary records circuit breaker failures before rethrowing.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TransportPublishFailurePolicy.RecordFailureIfApplicable(_circuitBreaker, exception);
            throw;
        }
    }
}
