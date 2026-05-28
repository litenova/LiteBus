using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents one persisted command row in a inbox store.
/// </summary>
/// <remarks>
///     Store implementations use this non-generic envelope so one table can hold many command types. The payload is the
///     serialized command; the contract fields identify the CLR type used for deserialization. Processors update status,
///     attempt count, lease, and error fields as the command moves through execution.
/// </remarks>
public sealed record InboxCommandEnvelope
{
    /// <summary>
    ///     Gets the unique persisted command identifier.
    /// </summary>
    public required Guid CommandId { get; init; }

    /// <summary>
    ///     Gets the stable command contract name used to resolve the command type.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the command contract version used to resolve the command type and payload shape.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the serialized command payload. The default PostgreSQL store writes this value to a `jsonb` column.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the command was accepted.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp at which the command may be processed.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }

    /// <summary>
    ///     Gets the number of processing attempts. Stores increment this value when a command is leased.
    /// </summary>
    public required int AttemptCount { get; init; }

    /// <summary>
    ///     Gets the current processing status.
    /// </summary>
    public required InboxCommandStatus Status { get; init; }

    /// <summary>
    ///     Gets the optional idempotency key used to detect duplicate submissions.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    ///     Gets the optional processing lease owner that currently holds the command.
    /// </summary>
    public string? LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the optional UTC timestamp when the processing lease expires.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>
    ///     Gets the optional latest processing error captured for diagnostics and dead-letter review.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier.
    /// </summary>
    public string? TenantId { get; init; }
}