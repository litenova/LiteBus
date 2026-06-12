using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts messages for persistence in the same Entity Framework Core transaction as domain state.
/// </summary>
/// <typeparam name="TContext">
///     The application <c>DbContext</c> type configured through <c>UseDbContext&lt;TContext&gt;()</c> on the EF Core inbox
///     storage builder.
/// </typeparam>
/// <remarks>
///     Register Entity Framework Core inbox storage with <c>EnableSaveChangesInterceptor()</c> to resolve
///     <see cref="ITransactionalInbox{TContext}" /> from a scoped service provider. Implementations stage envelopes
///     through
///     the EF Core save-changes interceptor so contract resolution and serialization follow the same path as
///     <see cref="IInbox" />. Contract lookup always uses <c>message.GetType()</c> for each instance. Use
///     <see cref="ITransactionalInboxStore" /> when callers already build <see cref="InboxEnvelope" /> instances and need
///     a
///     context-bound store writer.
/// </remarks>
public interface ITransactionalInbox<TContext>
    where TContext : class
{
    /// <summary>
    ///     Accepts a message for persistence when the scoped <typeparamref name="TContext" /> saves changes.
    /// </summary>
    /// <typeparam name="TMessage">
    ///     The compile-time message type. <c>item.Message.GetType()</c> is always used for contract lookup.
    /// </typeparam>
    /// <param name="item">The message payload and per-message acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    ///     Accepts a message for persistence when the scoped <typeparamref name="TContext" /> saves changes using default
    ///     acceptance metadata.
    /// </summary>
    /// <typeparam name="TMessage">
    ///     The compile-time message type. <c>message.GetType()</c> is always used for contract lookup.
    /// </typeparam>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
        => AcceptAsync(InboxAcceptItem<TMessage>.From(message), cancellationToken);

    /// <summary>
    ///     Accepts multiple messages for persistence in one staging pass before <c>SaveChanges</c>.
    /// </summary>
    /// <param name="items">The message payloads and per-message acceptance metadata to stage.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing staged inbox messages in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default);
}
