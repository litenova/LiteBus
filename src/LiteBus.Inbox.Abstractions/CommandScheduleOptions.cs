using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines metadata used when a command is accepted into the durable inbox.
/// </summary>
/// <remarks>
///     <para>
///         These values describe storage and processing policy for one scheduled command. They are not serialized into
///         the command payload, so they can change without changing the command contract.
///     </para>
///     <para>
///         Use <see cref="CommandId" /> or <see cref="IdempotencyKey" /> when a caller can retry the same request.
///         Use <see cref="VisibleAfter" /> for delayed execution. Use correlation, causation, and tenant fields for
///         tracing and isolation across durable processing.
///     </para>
/// </remarks>
public sealed record CommandScheduleOptions
{
    /// <summary>
    ///     Gets the optional command identifier. When omitted, the scheduler creates a new identifier.
    ///     Supply this value when an upstream request already has a durable operation id.
    /// </summary>
    public Guid? CommandId { get; init; }

    /// <summary>
    ///     Gets the optional idempotency key used to detect duplicate command submissions.
    ///     If this value is empty and the command implements <see cref="IIdempotentCommand" />, the scheduler copies
    ///     the key from the command.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp at which the command may be processed. A null value means the command is due
    ///     as soon as a processor leases it.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier used to group logs, traces, and stored messages for one workflow.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier used to identify the message or request that caused this command.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier used by multi-tenant applications and operational tooling.
    /// </summary>
    public string? TenantId { get; init; }
}