namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Identifies a topic subscription used for a non-destructive connectivity diagnostic.
/// </summary>
/// <param name="TopicName">The topic containing the subscription.</param>
/// <param name="SubscriptionName">The subscription name supplied to the Service Bus receiver.</param>
public sealed record AzureServiceBusSubscriptionDiagnosticTarget(
    string TopicName,
    string SubscriptionName) : AzureServiceBusDiagnosticTarget;
