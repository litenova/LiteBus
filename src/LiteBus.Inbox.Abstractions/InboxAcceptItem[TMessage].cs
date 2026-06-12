using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions.DurableMessaging;

#pragma warning disable CA1000 // Static factories on generic acceptance items preserve typed writer ergonomics.

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes one message acceptance unit for typed writer calls.
/// </summary>
/// <typeparam name="TMessage">The concrete message type carried in the item.</typeparam>
/// <remarks>
///     Contract lookup at runtime always uses <c>message.GetType()</c> for each instance. Use
///     <see cref="InboxAcceptItem" /> when building heterogeneous batches.
/// </remarks>
[DebuggerDisplay("MessageType = {typeof(TMessage).Name}")]
public sealed record InboxAcceptItem<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required TMessage Message { get; init; }

    /// <summary>
    ///     Gets per-message acceptance metadata applied outside the payload.
    /// </summary>
    public InboxAcceptMetadata Metadata { get; init; } = InboxAcceptMetadata.Immediate;

    /// <summary>
    ///     Creates a typed acceptance item with optional metadata.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Immediate" /> is used.
    /// </param>
    /// <returns>A typed acceptance item ready for <see cref="IInbox.AcceptAsync{TMessage}(InboxAcceptItem{TMessage}, System.Threading.CancellationToken)" />.</returns>
    public static InboxAcceptItem<TMessage> From(TMessage message, InboxAcceptMetadata? metadata = null)
    {
        return new InboxAcceptItem<TMessage>
        {
            Message = message,
            Metadata = metadata ?? InboxAcceptMetadata.Immediate
        };
    }

    /// <summary>
    ///     Creates an acceptance item that defers processor leasing until the specified UTC timestamp.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the message may be leased.</param>
    /// <returns>An acceptance item with <see cref="MessageVisibility.At" /> visibility metadata.</returns>
    public static InboxAcceptItem<TMessage> ScheduledAt(TMessage message, DateTimeOffset visibleAfter)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Visibility = new MessageVisibility.At(visibleAfter)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that defers processor leasing until a relative delay elapses.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="delay">The non-negative delay before the message becomes visible.</param>
    /// <returns>An acceptance item with <see cref="MessageVisibility.After" /> visibility metadata.</returns>
    public static InboxAcceptItem<TMessage> ScheduledAfter(TMessage message, TimeSpan delay)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Visibility = new MessageVisibility.After(delay)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores an application-defined idempotency key with the envelope.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="idempotencyKey">The idempotency key used for insert-time deduplication.</param>
    /// <returns>An acceptance item with <see cref="Idempotency.Keyed" /> metadata.</returns>
    public static InboxAcceptItem<TMessage> WithIdempotency(TMessage message, string idempotencyKey)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Idempotency = new Idempotency.Keyed(idempotencyKey)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores a caller-supplied inbox message identifier.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="messageId">The message identifier supplied by the caller.</param>
    /// <returns>An acceptance item with <see cref="MessageIdentity.Supplied" /> metadata.</returns>
    public static InboxAcceptItem<TMessage> WithIdentity(TMessage message, Guid messageId)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores a correlation identifier for distributed tracing.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="correlationId">The correlation identifier persisted with the envelope.</param>
    /// <returns>An acceptance item with <see cref="MessageTrace.Correlated" /> metadata.</returns>
    public static InboxAcceptItem<TMessage> WithCorrelation(TMessage message, string correlationId)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Correlated(correlationId)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores distributed trace metadata with the envelope.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="trace">The trace metadata persisted outside the payload.</param>
    /// <returns>An acceptance item carrying the supplied trace metadata.</returns>
    public static InboxAcceptItem<TMessage> WithTrace(TMessage message, MessageTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with { Trace = trace }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores tenant isolation metadata with the envelope.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="tenantId">The tenant identifier persisted with the envelope.</param>
    /// <returns>An acceptance item with <see cref="TenantScope.Isolated" /> metadata.</returns>
    public static InboxAcceptItem<TMessage> WithTenant(TMessage message, string tenantId)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Tenant = new TenantScope.Isolated(tenantId)
            }
        };
    }
}

#pragma warning restore CA1000

