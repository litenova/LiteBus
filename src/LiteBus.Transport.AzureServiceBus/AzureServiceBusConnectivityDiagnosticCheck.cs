using Azure.Messaging.ServiceBus;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Verifies Azure Service Bus connectivity by peeking a configured queue or subscription without settling messages.
/// </summary>
public sealed class AzureServiceBusConnectivityDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the shared Service Bus client used to create a diagnostic receiver.
    /// </summary>
    private readonly ServiceBusClient _client;

    /// <summary>
    ///     Gets the transport settings containing the diagnostic target.
    /// </summary>
    private readonly AzureServiceBusTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusConnectivityDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="client">The shared Service Bus client used to create a diagnostic receiver.</param>
    /// <param name="options">The transport settings containing the diagnostic target.</param>
    public AzureServiceBusConnectivityDiagnosticCheck(
        ServiceBusClient client,
        AzureServiceBusTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public string Name => "transport.azure_service_bus.connectivity";

    /// <inheritdoc />
    /// <remarks>
    ///     Peeking does not lock or settle a message. The final <see cref="Exception" /> handler prevents an adapter
    ///     diagnostic from escaping into sibling probes.
    /// </remarks>
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ConnectivityCheckTarget is null)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Degraded,
                "Azure Service Bus connectivity is not configured; set ConnectivityCheckTarget.");
        }

        try
        {
            var receiver = CreateReceiver(_options.ConnectivityCheckTarget);
            await using (receiver.ConfigureAwait(false))
            {
                await receiver.PeekMessagesAsync(1, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "Azure Service Bus entity is available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CreateUnhealthyResult();
        }
        catch (ServiceBusException)
        {
            return CreateUnhealthyResult();
        }
#pragma warning disable CA1031 // Diagnostic probes must map unexpected failures to unhealthy results without throwing.
        catch (Exception)
#pragma warning restore CA1031
        {
            return CreateUnhealthyResult();
        }
    }

    /// <summary>
    ///     Creates a receiver for the configured queue or subscription target.
    /// </summary>
    /// <param name="target">The diagnostic target.</param>
    /// <returns>The receiver used for one non-destructive peek.</returns>
    private ServiceBusReceiver CreateReceiver(AzureServiceBusDiagnosticTarget target)
    {
        return target switch
        {
            AzureServiceBusQueueDiagnosticTarget queue => _client.CreateReceiver(queue.QueueName),
            AzureServiceBusSubscriptionDiagnosticTarget subscription =>
                _client.CreateReceiver(subscription.TopicName, subscription.SubscriptionName),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported diagnostic target.")
        };
    }

    /// <summary>
    ///     Creates an unhealthy result without exposing SDK exception text.
    /// </summary>
    /// <returns>The unhealthy diagnostic result.</returns>
    private static DiagnosticResult CreateUnhealthyResult()
    {
        return new DiagnosticResult(
            DiagnosticStatus.Unhealthy,
            "Azure Service Bus entity is unavailable.");
    }
}
