using System;
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
///     <see cref="ITransactionalInbox{TContext}" /> from a scoped service provider. Implementations stage envelopes through
///     the EF Core save-changes interceptor so contract resolution and serialization follow the same path as
///     <see cref="IInbox" />. Contract lookup always uses <c>message.GetType()</c> for each instance. Use
///     <see cref="ITransactionalInboxStore" /> when callers already build <see cref="InboxEnvelope" /> instances and need a
///     context-bound store writer.
/// </remarks>
public interface ITransactionalInbox<TContext>
    where TContext : class
{
    /// <summary>
    ///     Accepts a message for persistence when the scoped <typeparamref name="TContext" /> saves changes using an explicit
    ///     runtime type.
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
    ///     Accepts a message for persistence when the scoped <typeparamref name="TContext" /> saves changes.
    /// </summary>
    /// <typeparam name="T">The compile-time message type. The runtime type of <paramref name="message" /> is used for contract lookup.</typeparam>
    /// <param name="message">The message instance to serialize and stage.</param>
    /// <param name="options">Optional acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt<T>> AcceptAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    ///     Accepts multiple messages for persistence in one staging pass before <c>SaveChanges</c>.
    /// </summary>
    /// <param name="messages">The message instances to serialize and stage.</param>
    /// <param name="messageTypes">
    ///     The runtime message types used for contract lookup. Must contain the same number of entries as
    ///     <paramref name="messages" />.
    /// </param>
    /// <param name="options">
    ///     Optional per-message metadata aligned with <paramref name="messages" />. When omitted, default metadata is used
    ///     for every message. When supplied, the list length must match <paramref name="messages" />.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing staged inbox messages in the same order as <paramref name="messages" />.
    /// </returns>
    Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<Type> messageTypes,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Accepts multiple messages for persistence in one staging pass before <c>SaveChanges</c>.
    /// </summary>
    /// <typeparam name="T">The shared compile-time message type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="messages">The message instances to serialize and stage.</param>
    /// <param name="options">
    ///     Optional per-message metadata aligned with <paramref name="messages" />. When omitted, default metadata is used
    ///     for every message.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing staged inbox messages in the same order as <paramref name="messages" />.
    /// </returns>
    Task<IReadOnlyList<InboxReceipt<T>>> AcceptBatchAsync<T>(
        IReadOnlyList<T> messages,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    ///     Accepts a message with <see cref="InboxOptions.VisibleAfter" /> set to <paramref name="enqueueAt" />.
    /// </summary>
    /// <typeparam name="T">The message type being staged.</typeparam>
    /// <param name="message">The message instance to serialize and stage.</param>
    /// <param name="enqueueAt">The UTC timestamp when the message becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt<T>> ScheduleAsync<T>(
        T message,
        DateTimeOffset enqueueAt,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    ///     Accepts a message with <see cref="InboxOptions.VisibleAfter" /> set relative to the current UTC time.
    /// </summary>
    /// <typeparam name="T">The message type being staged.</typeparam>
    /// <param name="message">The message instance to serialize and stage.</param>
    /// <param name="delay">The delay before the message becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged inbox message.</returns>
    Task<InboxReceipt<T>> ScheduleAfterAsync<T>(
        T message,
        TimeSpan delay,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;
}
