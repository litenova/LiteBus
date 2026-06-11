using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts messages into storage for later execution by an inbox processor.
/// </summary>
/// <remarks>
///     <para>
///         Use this API when the current caller should receive an acceptance receipt instead of waiting for a handler
///         to run. Calling <see cref="AcceptAsync{TMessage}" /> records an inbox envelope and returns after the backing
///         store accepts it.
///     </para>
///     <para>
///         Register each stored message type in <see cref="LiteBus.Messaging.Abstractions.IMessageContractRegistry" />
///         with a
///         stable name and version, or apply <see cref="LiteBus.Messaging.Abstractions.MessageContractAttribute" /> and
///         scan the
///         assembly during module configuration. Closed generic types are supported when each closed shape is registered.
///         Open generic contract definitions are rejected because the persisted payload must map back to one concrete CLR
///         type.
///     </para>
///     <para>
///         Delayed execution is expressed through <see cref="InboxAcceptMetadata.Visibility" /> on the acceptance item.
///         There is no separate scheduler interface on the writer surface.
///     </para>
/// </remarks>
public interface IInbox
{
    /// <summary>
    ///     Accepts a message for later execution by an inbox processor.
    /// </summary>
    /// <typeparam name="TMessage">
    ///     The compile-time message type. <c>item.Message.GetType()</c> is always used for contract lookup.
    /// </typeparam>
    /// <param name="item">The message payload and per-message acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     A receipt containing the message id, contract reference, acceptance time, trace metadata, and tenant scope.
    /// </returns>
    Task<InboxReceipt> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    ///     Accepts multiple messages for later execution in one store round trip.
    /// </summary>
    /// <param name="items">The message payloads and per-message acceptance metadata to store.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     Receipts containing message ids, contract references, acceptance times, trace metadata, and tenant scopes in
    ///     the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default);
}