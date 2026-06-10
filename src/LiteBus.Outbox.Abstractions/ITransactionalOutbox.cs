using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Enqueues events for persistence in the same Entity Framework Core transaction as domain state.
/// </summary>
/// <typeparam name="TContext">
///     The application <c>DbContext</c> type configured through <c>UseDbContext&lt;TContext&gt;()</c> on the EF Core outbox
///     storage builder.
/// </typeparam>
/// <remarks>
///     Register Entity Framework Core outbox storage with <c>EnableSaveChangesInterceptor()</c> to resolve
///     <see cref="ITransactionalOutbox{TContext}" /> from a scoped service provider. Implementations stage envelopes through
///     the EF Core save-changes interceptor so contract resolution and serialization follow the same path as
///     <see cref="IOutbox" />. Contract lookup always uses <c>event.GetType()</c> for each instance. Use
///     <see cref="ITransactionalOutboxStore" /> when callers already build <see cref="OutboxEnvelope" /> instances and need a
///     context-bound store writer.
/// </remarks>
public interface ITransactionalOutbox<TContext>
    where TContext : class
{
    /// <summary>
    ///     Enqueues an event for persistence when the scoped <typeparamref name="TContext" /> saves changes.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type of <paramref name="event" /> is used for contract lookup.</typeparam>
    /// <param name="event">The event instance to serialize and stage.</param>
    /// <param name="options">Optional enqueue metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged outbox message.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues multiple events for persistence in one staging pass before <c>SaveChanges</c>.
    /// </summary>
    /// <param name="events">The event instances to serialize and stage.</param>
    /// <param name="eventTypes">
    ///     The runtime event types used for contract lookup. Must contain the same number of entries as
    ///     <paramref name="events" />.
    /// </param>
    /// <param name="options">
    ///     Optional per-event metadata aligned with <paramref name="events" />. When omitted, default metadata is used for
    ///     every event. When supplied, the list length must match <paramref name="events" />.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing staged outbox messages in the same order as <paramref name="events" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Enqueues multiple events for persistence in one staging pass before <c>SaveChanges</c>.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="events">The event instances to serialize and stage.</param>
    /// <param name="options">
    ///     Optional per-event metadata aligned with <paramref name="events" />. When omitted, default metadata is used for
    ///     every event.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing staged outbox messages in the same order as <paramref name="events" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues an event with <see cref="OutboxOptions.VisibleAfter" /> set to <paramref name="enqueueAt" />.
    /// </summary>
    /// <typeparam name="TEvent">The event type being staged.</typeparam>
    /// <param name="event">The event instance to serialize and stage.</param>
    /// <param name="enqueueAt">The UTC timestamp when the event becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged outbox message.</returns>
    Task<OutboxReceipt<TEvent>> ScheduleAsync<TEvent>(
        TEvent @event,
        DateTimeOffset enqueueAt,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues an event with <see cref="OutboxOptions.VisibleAfter" /> set relative to the current UTC time.
    /// </summary>
    /// <typeparam name="TEvent">The event type being staged.</typeparam>
    /// <param name="event">The event instance to serialize and stage.</param>
    /// <param name="delay">The delay before the event becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged outbox message.</returns>
    Task<OutboxReceipt<TEvent>> ScheduleAfterAsync<TEvent>(
        TEvent @event,
        TimeSpan delay,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
