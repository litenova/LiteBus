using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes per-event durable metadata applied when an event is enqueued into the outbox.
/// </summary>
/// <remarks>
///     <para>
///         Metadata stays outside the event payload so publication routing, visibility, and trace context can evolve
///         independently from the event contract. Use <see cref="Immediate" /> when no deferred visibility, idempotency,
///         trace, tenant, or publication override is required.
///     </para>
///     <para>
///         Scheduling is expressed through <see cref="Visibility" /> rather than separate scheduler interfaces.
///     </para>
/// </remarks>
public sealed record OutboxEnqueueMetadata
{
    /// <summary>
    ///     Gets metadata that enqueues the event for immediate processor leasing with generated identity and contract-default
    ///     publication routing.
    /// </summary>
    public static OutboxEnqueueMetadata Immediate { get; } = new()
    {
        Identity = MessageIdentity.Generated.Instance,
        Idempotency = Idempotency.None.Instance,
        Visibility = MessageVisibility.Immediate.Instance,
        Trace = MessageTrace.None.Instance,
        Tenant = TenantScope.Unscoped.Instance,
        Target = PublicationTarget.ContractDefault.Instance
    };

    /// <summary>
    ///     Gets how the outbox message identifier is assigned for the enqueue operation.
    /// </summary>
    public required MessageIdentity Identity { get; init; }

    /// <summary>
    ///     Gets the idempotency metadata used to collapse duplicate enqueue attempts into one stored row.
    /// </summary>
    public required Idempotency Idempotency { get; init; }

    /// <summary>
    ///     Gets when the stored event becomes eligible for processor leasing.
    /// </summary>
    public required MessageVisibility Visibility { get; init; }

    /// <summary>
    ///     Gets the distributed trace metadata persisted with the outbox envelope.
    /// </summary>
    public required MessageTrace Trace { get; init; }

    /// <summary>
    ///     Gets the tenant isolation metadata persisted with the outbox envelope.
    /// </summary>
    public required TenantScope Tenant { get; init; }

    /// <summary>
    ///     Gets how dispatchers resolve the publication destination for the stored event.
    /// </summary>
    public required PublicationTarget Target { get; init; }
}