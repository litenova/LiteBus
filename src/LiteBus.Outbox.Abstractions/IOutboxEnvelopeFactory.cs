using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Creates outbox envelopes from event instances using contract lookup, serialization, and payload protection.
/// </summary>
/// <remarks>
///     Envelope factories centralize enqueue metadata mapping so <see cref="IOutbox" />,
///     <see cref="ITransactionalOutbox" />, and Entity Framework Core staging share one creation path.
/// </remarks>
public interface IOutboxEnvelopeFactory
{
    /// <summary>
    ///     Creates one outbox envelope from an event instance and optional enqueue metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type of <paramref name="event" /> is used for contract lookup.</typeparam>
    /// <param name="event">The event instance to serialize.</param>
    /// <param name="options">Optional enqueue metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The outbox envelope ready for store persistence or staging.</returns>
    Task<OutboxEnvelope> CreateAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Creates multiple outbox envelopes from event instances and optional per-event metadata.
    /// </summary>
    /// <param name="events">The event instances to serialize.</param>
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
    ///     Outbox envelopes in the same order as <paramref name="events" />.
    /// </returns>
    Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates multiple outbox envelopes from event instances sharing one compile-time type.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="events">The event instances to serialize.</param>
    /// <param name="options">
    ///     Optional per-event metadata aligned with <paramref name="events" />. When omitted, default metadata is used for
    ///     every event.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Outbox envelopes in the same order as <paramref name="events" />.
    /// </returns>
    Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
