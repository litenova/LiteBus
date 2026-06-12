using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts messages for persistence in the caller's active transaction boundary.
/// </summary>
/// <remarks>
///     Use this API when domain work and inbox acceptance must commit or roll back together outside Entity Framework Core.
///     Bind a store through <see cref="ITransactionalInboxStore" /> or register PostgreSQL ambient participation with
///     <c>EnableAmbientTransactionProvider()</c>. The caller owns transaction commit. Processors continue to use
///     <see cref="IInbox" /> with the singleton auto-commit store.
/// </remarks>
public interface ITransactionalInbox
{
    /// <summary>
    ///     Accepts a message for persistence in the caller's active transaction.
    /// </summary>
    /// <typeparam name="TMessage">
    ///     The compile-time message type. <c>item.Message.GetType()</c> is always used for contract lookup.
    /// </typeparam>
    /// <param name="item">The message payload and per-message acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the accepted inbox message.</returns>
    Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    ///     Accepts a message for persistence in the caller's active transaction using default acceptance metadata.
    /// </summary>
    /// <typeparam name="TMessage">
    ///     The compile-time message type. <c>message.GetType()</c> is always used for contract lookup.
    /// </typeparam>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the accepted inbox message.</returns>
    Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
        => AcceptAsync(InboxAcceptItem<TMessage>.From(message), cancellationToken);

    /// <summary>
    ///     Accepts multiple messages for persistence in one store round trip within the caller's transaction.
    /// </summary>
    /// <param name="items">The message payloads and per-message acceptance metadata to persist.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing accepted inbox messages in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default);
}
