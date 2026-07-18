namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Identifies a queue used for a non-destructive connectivity diagnostic.
/// </summary>
/// <param name="QueueName">The queue name supplied to the Service Bus receiver.</param>
public sealed record AzureServiceBusQueueDiagnosticTarget(string QueueName) : AzureServiceBusDiagnosticTarget;
