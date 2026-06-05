using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Schedules events for outbox publication at a future time.
/// </summary>
public interface IOutboxScheduler
{
    /// <summary>
    ///     Enqueues an event into the outbox with <see cref="OutboxOptions.VisibleAfter" /> set to
    ///     <paramref name="enqueueAt" />.
    /// </summary>
    /// <typeparam name="TEvent">The event type being stored.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="enqueueAt">The UTC timestamp when the event becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt for the stored event.</returns>
    Task<OutboxReceipt<TEvent>> ScheduleAsync<TEvent>(
        TEvent @event,
        DateTimeOffset enqueueAt,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues an event into the outbox with <see cref="OutboxOptions.VisibleAfter" /> set relative to the current UTC
    ///     time.
    /// </summary>
    /// <typeparam name="TEvent">The event type being stored.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="delay">The delay before the event becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt for the stored event.</returns>
    Task<OutboxReceipt<TEvent>> ScheduleAfterAsync<TEvent>(
        TEvent @event,
        TimeSpan delay,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
