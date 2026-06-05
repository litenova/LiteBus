namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Connection settings for Azure Service Bus transport adapters.
/// </summary>
public sealed class AzureServiceBusTransportOptions
{
    /// <summary>
    ///     Gets the Service Bus connection string used to create the shared client.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    ///     Gets the optional client identifier passed to the Service Bus client.
    /// </summary>
    public string? ClientId { get; init; }
}

