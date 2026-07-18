namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Connection settings for Azure Service Bus transport adapters.
/// </summary>
public sealed record AzureServiceBusTransportOptions
{
    /// <summary>
    ///     Gets the Service Bus connection string used to create the shared client.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    ///     Gets the optional client identifier passed to the Service Bus client.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    ///     Gets the queue or subscription target peeked by the connectivity diagnostic.
    /// </summary>
    /// <value>
    ///     When <see langword="null" />, the diagnostic reports degraded because opening a client does not establish a
    ///     broker connection.
    /// </value>
    public AzureServiceBusDiagnosticTarget? ConnectivityCheckTarget { get; init; }

    /// <summary>
    ///     Gets the delay applied before restarting the processor after a recoverable processing error.
    /// </summary>
    public TimeSpan ConsumerErrorRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the maximum delay between processor restart attempts after repeated failures.
    /// </summary>
    public TimeSpan ConsumerErrorRetryMaxInterval { get; init; } = TimeSpan.FromMinutes(1);
}
