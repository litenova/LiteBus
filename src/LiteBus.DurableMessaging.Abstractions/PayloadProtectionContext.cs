namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Identifies the immutable durable metadata that authenticated payload encryption should bind to ciphertext.
/// </summary>
public sealed record PayloadProtectionContext
{
    /// <summary>
    ///     Gets the durable message identifier.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>
    ///     Gets the contract name stored beside the payload.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the contract version stored beside the payload.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier stored beside the payload.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets the durable axis that owns the payload, either <c>inbox</c> or <c>outbox</c>.
    /// </summary>
    public required string Axis { get; init; }
}
