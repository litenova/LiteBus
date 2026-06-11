namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes one message acceptance unit for typed writer calls.
/// </summary>
/// <typeparam name="TMessage">The compile-time message type carried in the item.</typeparam>
/// <remarks>
///     Contract lookup at runtime always uses <c>message.GetType()</c> for each instance. Use
///     <see cref="InboxAcceptItem" /> when building heterogeneous batches.
/// </remarks>
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
    public InboxAcceptMetadata Metadata { get; init; } = InboxAcceptMetadata.Default;
}

/// <summary>
///     Describes one message acceptance unit for batch writer calls.
/// </summary>
/// <remarks>
///     Use this shape when a single batch contains messages of different CLR types. Contract lookup uses
///     <c>message.GetType()</c> for each entry.
/// </remarks>
public sealed record InboxAcceptItem
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required object Message { get; init; }

    /// <summary>
    ///     Gets per-message acceptance metadata applied outside the payload.
    /// </summary>
    public InboxAcceptMetadata Metadata { get; init; } = InboxAcceptMetadata.Default;
}