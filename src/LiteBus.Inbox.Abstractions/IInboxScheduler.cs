using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Schedules messages for inbox processing at a future time.
/// </summary>
public interface IInboxScheduler
{
    /// <summary>
    ///     Accepts a message into the inbox with <see cref="InboxOptions.VisibleAfter" /> set to
    ///     <paramref name="enqueueAt" />.
    /// </summary>
    /// <typeparam name="T">The message type being stored.</typeparam>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="enqueueAt">The UTC timestamp when the message becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt for the stored message.</returns>
    Task<InboxReceipt<T>> ScheduleAsync<T>(
        T message,
        DateTimeOffset enqueueAt,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    ///     Accepts a message into the inbox with <see cref="InboxOptions.VisibleAfter" /> set relative to the current UTC
    ///     time.
    /// </summary>
    /// <typeparam name="T">The message type being stored.</typeparam>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="delay">The delay before the message becomes visible to processors.</param>
    /// <param name="options">Optional metadata applied in addition to the scheduled visibility time.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt for the stored message.</returns>
    Task<InboxReceipt<T>> ScheduleAfterAsync<T>(
        T message,
        TimeSpan delay,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;
}
