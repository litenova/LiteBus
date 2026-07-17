namespace LiteBus.Inbox.Ingress.AwsSqs;

/// <summary>
///     Configures AWS SQS inbox ingress.
/// </summary>
public sealed record AwsSqsInboxIngressOptions
{
    /// <summary>
    ///     Gets the provider-neutral ingress safety settings.
    /// </summary>
    public TransportInboxIngressSafetyOptions Safety { get; init; } = new();

    /// <summary>
    ///     Gets the queue URL the ingress consumer polls.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the maximum number of messages requested per receive call.
    /// </summary>
    public ushort PrefetchCount { get; init; }

    /// <summary>
    ///     Gets a value indicating whether failed store writes should return messages for retry.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;
}
