using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts messages into storage for later execution by an inbox processor.
/// </summary>
/// <remarks>
///     <para>
///         Use this API when the current caller should receive an acceptance receipt instead of waiting for a handler
///         to run. Calling <c>AddAsync</c> records an inbox envelope and returns after the backing store accepts it.
///     </para>
///     <para>
///         Register each stored message type in <c>IMessageContractRegistry</c> with a stable name and version. Closed
///         generic types are supported when each closed shape is registered. Open generic contract definitions are
///         rejected because the persisted payload must map back to one concrete CLR type.
///     </para>
/// </remarks>
public interface IInbox
{
    /// <summary>
    ///     Stores a message for later execution by an inbox processor.
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
    Task<InboxReceipt<T>> AddAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;
}