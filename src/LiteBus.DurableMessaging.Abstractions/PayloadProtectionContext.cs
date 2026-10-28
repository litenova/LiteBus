using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Identifies the immutable durable metadata that authenticated payload encryption should bind to ciphertext.
/// </summary>
/// <remarks>
///     The tenant identifier is normalized so <see langword="null" />, empty, and whitespace values all read back as
///     <see langword="null" /> for unscoped messages.
/// </remarks>
public sealed record PayloadProtectionContext
{
    /// <summary>
    ///     Gets the normalized tenant identifier stored beside the payload.
    /// </summary>
    private string? _tenantId;

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
    /// <value>
    ///     The tenant identifier, or <see langword="null" /> when the message is unscoped. Empty and whitespace values normalize to <see langword="null" />.
    /// </value>
    public string? TenantId
    {
        get => _tenantId;
        init => _tenantId = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    ///     Gets the durable axis that owns the payload, either <c>inbox</c> or <c>outbox</c>.
    /// </summary>
    public required string Axis { get; init; }
}
