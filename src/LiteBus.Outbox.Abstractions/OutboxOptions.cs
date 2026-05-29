using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines metadata used when an event is accepted into the outbox.
/// </summary>
/// <remarks>
///     <para>
///         These values describe the stored envelope and publication target. They are not serialized into the event
///         payload, so they can change independently from the event contract.
///     </para>
///     <para>
///         Use <see cref="MessageId" /> for idempotent writes when the application already has a stable event id. The
///         former identified-event marker was removed; the outbox message id now belongs to options rather than the
///         event contract.
///     </para>
/// </remarks>
public sealed record OutboxOptions
{
    /// <summary>
    ///     Gets the optional outbox message identifier. When omitted, the writer creates a new identifier.
    /// </summary>
    public Guid? MessageId { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier used to group logs, traces, and stored messages for one workflow.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the optional causation identifier used to identify the message or request that caused this event.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier used by multi-tenant applications and operational tooling.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets the optional publication topic or channel. Dispatchers decide how this value maps to a transport target.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp at which the message may be published. A null value means the message is due
    ///     as soon as a processor leases it.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }
}