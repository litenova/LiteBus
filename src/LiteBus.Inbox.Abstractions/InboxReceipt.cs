using System;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents the result of accepting a message into the inbox.
/// </summary>
/// <remarks>
///     <para>
///         A receipt means the store accepted the inbox envelope. It does not mean a handler has run. Return this value
///         from an API as acceptance or tracking data, then use queries to read the state produced by later execution.
///     </para>
/// </remarks>
public sealed record InboxReceipt
{
    /// <summary>
    ///     Gets the unique message identifier that processors and tracking endpoints can use.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the CLR message type that was accepted. For closed generic messages, this is the closed runtime type.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the stable contract name and version stored with the payload.
    /// </summary>
    public required MessageContractReference Contract { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the message was accepted by the store.
    /// </summary>
    public required DateTimeOffset AcceptedAt { get; init; }

    /// <summary>
    ///     Gets the distributed tracing metadata copied from acceptance metadata or from the stored duplicate row.
    /// </summary>
    public required MessageTrace Trace { get; init; }

    /// <summary>
    ///     Gets the tenant isolation metadata copied from acceptance metadata or from the stored duplicate row.
    /// </summary>
    public required TenantScope Tenant { get; init; }
}

/// <summary>
///     Represents the typed result of accepting one message into the inbox.
/// </summary>
/// <typeparam name="TMessage">The compile-time message type associated with the acceptance command.</typeparam>
/// <remarks>
///     Typed receipts preserve caller intent at the API boundary while sharing the same persistence fields as
///     <see cref="InboxReceipt" /> returned from batch acceptance APIs.
/// </remarks>
public sealed record InboxReceipt<TMessage> where TMessage : notnull
{
    /// <summary>
    ///     Gets the unique message identifier that processors and tracking endpoints can use.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the CLR message type that was accepted. For closed generic messages, this is the closed runtime type.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the stable contract name and version stored with the payload.
    /// </summary>
    public required MessageContractReference Contract { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the message was accepted by the store.
    /// </summary>
    public required DateTimeOffset AcceptedAt { get; init; }

    /// <summary>
    ///     Gets the distributed tracing metadata copied from acceptance metadata or from the stored duplicate row.
    /// </summary>
    public required MessageTrace Trace { get; init; }

    /// <summary>
    ///     Gets the tenant isolation metadata copied from acceptance metadata or from the stored duplicate row.
    /// </summary>
    public required TenantScope Tenant { get; init; }
}
