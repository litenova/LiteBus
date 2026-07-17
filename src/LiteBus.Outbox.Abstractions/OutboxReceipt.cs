using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents the result of accepting an event into the outbox.
/// </summary>
/// <remarks>
///     <para>
///         A receipt means the store accepted the outbox envelope. It does not mean a dispatcher has published the event
///         to its final target. Use the message id for diagnostics, replay tooling, or API acceptance responses.
///     </para>
/// </remarks>
[DebuggerDisplay("Id = {Id}, Outcome = {Outcome}")]
public sealed record OutboxReceipt
{
    /// <summary>
    ///     Gets the unique outbox message identifier used by processors and operational tooling.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the CLR message type that was stored. For closed generic types, this is the closed runtime type.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the stable event contract reference stored with the payload.
    /// </summary>
    public required MessageContractReference Contract { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the event was accepted by the store.
    /// </summary>
    public required DateTimeOffset StoredAt { get; init; }

    /// <summary>
    ///     Gets the distributed trace metadata copied from enqueue metadata or from the stored duplicate row.
    /// </summary>
    public MessageTrace Trace { get; init; } = MessageTrace.None.Instance;

    /// <summary>
    ///     Gets the tenant isolation metadata copied from enqueue metadata or from the stored duplicate row.
    /// </summary>
    public TenantScope Tenant { get; init; } = TenantScope.Unscoped.Instance;

    /// <summary>
    ///     Gets whether the store enqueued a new row or returned an existing one for the supplied idempotency metadata.
    /// </summary>
    public OutboxEnqueueOutcome Outcome { get; init; } = OutboxEnqueueOutcome.Enqueued;
}
