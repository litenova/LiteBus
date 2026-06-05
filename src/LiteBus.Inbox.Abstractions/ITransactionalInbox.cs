using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts messages for persistence in the same Entity Framework Core transaction as domain state.
/// </summary>
/// <remarks>
///     Register Entity Framework Core inbox storage with <c>EnableSaveChangesInterceptor()</c> to resolve
///     <see cref="ITransactionalInbox" />. Implementations stage envelopes through the EF Core save-changes interceptor so
///     contract resolution and serialization follow the same path as <see cref="IInbox" />. Use
///     <see cref="ITransactionalInboxStore" /> when callers already build <see cref="InboxEnvelope" /> instances and need a
///     context-bound store writer.
/// </remarks>
public interface ITransactionalInbox
{
    /// <summary>
    ///     Accepts a message for persistence when the active <c>DbContext</c> saves changes using an explicit runtime type.
    /// </summary>
    /// <param name="message">The message instance to serialize and stage.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="options">Optional acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt> AcceptAsync(
        object message,
        Type messageType,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Accepts a message for persistence when the active <c>DbContext</c> saves changes.
    /// </summary>
    /// <typeparam name="T">The compile-time message type. The runtime type is used for contract lookup.</typeparam>
    /// <param name="message">The message instance to serialize and stage.</param>
    /// <param name="options">Optional acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt<T>> AcceptAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;
}
