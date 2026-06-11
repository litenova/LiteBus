using LiteBus.Transport.Aws;

namespace LiteBus.Inbox.Ingress.Aws;

/// <summary>
///     Configures AWS SQS inbox ingress and connection settings.
/// </summary>
public sealed record AwsSqsInboxIngressOptions
{
    /// <summary>
    ///     Gets the SQS connection settings used by the ingress consumer.
    /// </summary>
    public required AwsSqsTransportOptions Connection { get; init; }

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