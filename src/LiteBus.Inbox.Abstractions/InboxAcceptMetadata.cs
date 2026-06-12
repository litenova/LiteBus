using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines per-message durable annotations applied when a message is accepted into the inbox.
/// </summary>
/// <remarks>
///     <para>
///         Metadata describes storage and processing policy for one stored message. It is not serialized into the message
///         payload, so acceptance annotations can change without changing the message contract.
///     </para>
///     <para>
///         Use <see cref="Identity" /> or <see cref="Idempotency" /> when a caller can retry the same request.
///         Use <see cref="Visibility" /> for delayed execution. Use <see cref="Trace" /> and <see cref="Tenant" /> for
///         distributed tracing and multi-tenant isolation.
///     </para>
/// </remarks>
public sealed record InboxAcceptMetadata
{
    /// <summary>
    ///     Gets metadata for immediate visibility with generated identity and no idempotency key.
    /// </summary>
    public static InboxAcceptMetadata Immediate { get; } = new()
    {
        Identity = MessageIdentity.Generated.Instance,
        Idempotency = Idempotency.None.Instance,
        Visibility = MessageVisibility.Immediate.Instance,
        Trace = MessageTrace.None.Instance,
        Tenant = TenantScope.Unscoped.Instance
    };

    /// <summary>
    ///     Gets how the message identifier is assigned when the envelope is stored.
    /// </summary>
    public required MessageIdentity Identity { get; init; }

    /// <summary>
    ///     Gets idempotency metadata used to detect duplicate submissions.
    /// </summary>
    public required Idempotency Idempotency { get; init; }

    /// <summary>
    ///     Gets when the message becomes eligible for processor leasing.
    /// </summary>
    public required MessageVisibility Visibility { get; init; }

    /// <summary>
    ///     Gets distributed tracing metadata persisted with the envelope.
    /// </summary>
    public required MessageTrace Trace { get; init; }

    /// <summary>
    ///     Gets tenant isolation metadata persisted with the envelope.
    /// </summary>
    public required TenantScope Tenant { get; init; }
}
