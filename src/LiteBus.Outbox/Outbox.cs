using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default writer that turns an event instance into an outbox envelope.
/// </summary>
/// <remarks>
///     <para>
///         The writer performs only acceptance work: contract lookup, serialization, metadata mapping, and append to the
///         configured <see cref="IOutboxStore" />. It does not publish the event. Publication belongs to
///         <see cref="PipelinedOutboxProcessor" /> and the configured <see cref="IOutboxDispatcher" />.
///     </para>
///     <para>
///         Contract lookup always uses <c>event.GetType()</c> so closed generic event instances are stored with the
///         contract
///         registered for that closed type. Stable message identity is supplied through
///         <see cref="OutboxEnqueueMetadata.Identity" />.
///     </para>
/// </remarks>
public sealed class Outbox : IOutbox
{
    /// <summary>
    ///     Gets the outbox writer store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IOutboxStore _store;

    /// <summary>
    ///     Gets the shared enqueue pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly OutboxWriterCore _writerCore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Outbox" /> class.
    /// </summary>
    /// <param name="store">The outbox writer store used to persist newly accepted envelopes.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    public Outbox(
        IOutboxStore store,
        IOutboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(envelopeFactory);

        _store = store;
        _writerCore = new OutboxWriterCore(envelopeFactory);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueAsync(item, (envelope, token) => _store.AddAsync(envelope, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueAsync(item, (envelope, token) => _store.AddAsync(envelope, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueBatchAsync(
            items,
            (envelopes, token) => _store.AddBatchAsync(envelopes, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueBatchAsync(
            items,
            (envelopes, token) => _store.AddBatchAsync(envelopes, token),
            cancellationToken);
    }
}
