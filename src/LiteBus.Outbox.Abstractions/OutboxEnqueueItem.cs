using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one event enqueue command with explicit runtime type information for contract lookup.
/// </summary>
/// <remarks>
///     Use this shape for heterogeneous batch enqueues where each event may resolve to a different contract.
///     Contract lookup always uses <see cref="EventType" /> rather than only the compile-time generic argument.
/// </remarks>
public sealed record OutboxEnqueueItem
{
    /// <summary>
    ///     Gets the event instance to serialize and store.
    /// </summary>
    public required object Event { get; init; }

    /// <summary>
    ///     Gets the runtime event type used for contract lookup.
    /// </summary>
    public required Type EventType { get; init; }

    /// <summary>
    ///     Gets the durable metadata applied when the event is enqueued.
    /// </summary>
    public OutboxEnqueueMetadata Metadata { get; init; } = OutboxEnqueueMetadata.Immediate;
}

/// <summary>
///     Represents one typed event enqueue command.
/// </summary>
/// <remarks>
///     Contract lookup uses <c>event.GetType()</c> for each instance. The compile-time type parameter documents caller
///     intent and enables typed receipts without a separate runtime type argument.
/// </remarks>
/// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
public sealed record OutboxEnqueueItem<TEvent>
    where TEvent : notnull
{
    /// <summary>
    ///     Gets the event instance to serialize and store.
    /// </summary>
    public required TEvent Event { get; init; }

    /// <summary>
    ///     Gets the durable metadata applied when the event is enqueued.
    /// </summary>
    public OutboxEnqueueMetadata Metadata { get; init; } = OutboxEnqueueMetadata.Immediate;
}