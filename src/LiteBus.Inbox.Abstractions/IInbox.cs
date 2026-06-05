using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts messages into storage for later execution by an inbox processor.
/// </summary>
/// <remarks>
///     <para>
///         Use this API when the current caller should receive an acceptance receipt instead of waiting for a handler
///         to run. Calling <see cref="AcceptAsync" /> records an inbox envelope and returns after the backing store accepts it.
///     </para>
///     <para>
///         Register each stored message type in <see cref="LiteBus.Messaging.Abstractions.IMessageContractRegistry" /> with a
///         stable name and version, or apply <see cref="LiteBus.Messaging.Abstractions.MessageContractAttribute" /> and scan the
///         assembly during module configuration. Closed generic types are supported when each closed shape is registered.
///         Open generic contract definitions are rejected because the persisted payload must map back to one concrete CLR type.
///     </para>
/// </remarks>
public interface IInbox
{
    /// <summary>
    ///     Accepts a message for later execution by an inbox processor using an explicit runtime type.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="options">
    ///     Optional message metadata such as a caller-supplied id, idempotency key, first visible timestamp,
    ///     correlation id, causation id, and tenant id. Metadata stays outside the message payload.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     A receipt containing the message id, contract name, version, acceptance time, and trace metadata.
    /// </returns>
    Task<InboxReceipt> AcceptAsync(
        object message,
        Type messageType,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Accepts a message for later execution by an inbox processor.
    /// </summary>
    /// <typeparam name="T">The message type being stored. The runtime type is used for contract lookup.</typeparam>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="options">
    ///     Optional message metadata such as a caller-supplied id, idempotency key, first visible timestamp,
    ///     correlation id, causation id, and tenant id. Metadata stays outside the message payload.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     A receipt containing the message id, contract name, version, acceptance time, and trace metadata.
    /// </returns>
    Task<InboxReceipt<T>> AcceptAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;
}
