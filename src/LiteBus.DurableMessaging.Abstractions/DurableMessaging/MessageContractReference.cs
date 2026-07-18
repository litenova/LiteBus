namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Identifies the persisted message contract without carrying the concrete CLR message type.
/// </summary>
/// <remarks>
///     <para>
///         Receipts and query results use this slim shape. Full contract registration includes
///         <see cref="MessageContract.MessageType" /> via <see cref="MessageContract" />.
///     </para>
/// </remarks>
public sealed record MessageContractReference
{
    /// <summary>
    ///     Gets the stable contract name written to stored envelopes.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Gets the positive contract version written to stored envelopes.
    /// </summary>
    public required int Version { get; init; }
}
