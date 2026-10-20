using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using Marten;

namespace LiteBus.Outbox.Storage.Marten;

/// <summary>
///     Enqueues outbox rows through the current Marten document session so documents and outbox rows commit together.
/// </summary>
/// <remarks>
///     <para>
///         Each staged envelope is queued on the session with <c>QueueSqlCommand</c> using the schema-owned insert
///         from <see cref="PostgreSqlOutboxSchema" />. The row is written only when the session
///         <c>SaveChangesAsync</c> commits; disposing the session without saving stores nothing.
///     </para>
///     <para>
///         A lookup reads committed rows on a separate connection, and a concurrent insert of the same id or key that
///         wins the race is absorbed by <c>ON CONFLICT DO NOTHING</c>, so the receipt then says
///         <see cref="OutboxEnqueueOutcome.Enqueued" /> though the other row was kept.
///     </para>
/// </remarks>
public sealed class MartenTransactionalOutbox : ITransactionalOutbox
{
    /// <summary>
    ///     Gets the document session that carries queued outbox inserts until <c>SaveChangesAsync</c>.
    /// </summary>
    private readonly IDocumentSession _session;

    /// <summary>
    ///     Gets the shared enqueue pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly OutboxWriterCore _writerCore;

    /// <summary>
    ///     Gets the store used for committed-row idempotency lookups on a separate connection.
    /// </summary>
    private readonly PostgreSqlOutboxStore _lookupStore;

    /// <summary>
    ///     Gets the outbox store options that select the schema and table of queued inserts.
    /// </summary>
    private readonly PostgreSqlOutboxStoreOptions _options;

    /// <summary>
    ///     Gets the envelopes already queued by this instance, keyed by message identifier.
    /// </summary>
    private readonly Dictionary<Guid, OutboxEnvelope> _queuedByMessageId = new();

    /// <summary>
    ///     Gets the envelopes already queued by this instance, keyed by tenant-scoped idempotency key.
    /// </summary>
    private readonly Dictionary<string, OutboxEnvelope> _queuedByIdempotencyScope = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenTransactionalOutbox" /> class.
    /// </summary>
    /// <param name="session">The document session that carries queued outbox inserts.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before session staging.</param>
    /// <param name="registration">The PostgreSQL store registration holding the data source and options.</param>
    public MartenTransactionalOutbox(
        IDocumentSession session,
        IOutboxEnvelopeFactory envelopeFactory,
        PostgreSqlOutboxStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(envelopeFactory);
        ArgumentNullException.ThrowIfNull(registration);

        _session = session;
        _writerCore = new OutboxWriterCore(envelopeFactory);
        _lookupStore = new PostgreSqlOutboxStore(registration.DataSource, registration.Options);
        _options = registration.Options;
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueAsync(item, StageAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueAsync(item, StageAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<OutboxReceipt>>([]);
        }

        return _writerCore.EnqueueBatchAsync(
            items,
            StageBatchAsync,
            cancellationToken);
    }

    /// <summary>
    ///     Stages one envelope through the document session or returns an existing row for duplicate submissions.
    /// </summary>
    /// <param name="envelope">The envelope created for the current enqueue attempt.</param>
    /// <param name="cancellationToken">The token used to cancel the lookup.</param>
    /// <returns>The staged or existing envelope with its enqueue outcome.</returns>
    private async Task<OutboxAppendResult> StageAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        var queued = FindQueuedEnvelope(envelope);

        if (queued is not null)
        {
            ThrowIfStrictConflict(envelope, queued);
            return new OutboxAppendResult(queued, OutboxEnqueueOutcome.AlreadyEnqueued);
        }

        var existing = await _lookupStore
            .FindExistingOrDefaultAsync(envelope.Id, envelope.TenantId, envelope.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            ThrowIfStrictConflict(envelope, existing);
            return new OutboxAppendResult(existing, OutboxEnqueueOutcome.AlreadyEnqueued);
        }

        var statement = PostgreSqlOutboxSchema.CreateInsertStatement(envelope, _options);
        _session.QueueSqlCommand('?', statement.FormatSql('?'), [.. statement.Values]);
        TrackQueuedEnvelope(envelope);

        return new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued);
    }

    /// <summary>
    ///     Stages each batch envelope in order so duplicates within the batch resolve against earlier items.
    /// </summary>
    /// <param name="envelopes">The envelopes created for the current enqueue batch.</param>
    /// <param name="cancellationToken">The token used to cancel existing-row lookups.</param>
    /// <returns>The staged or existing envelope outcome for every batch item.</returns>
    private async Task<IReadOnlyList<OutboxAppendResult>> StageBatchAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var staged = new OutboxAppendResult[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            staged[index] = await StageAsync(envelopes[index], cancellationToken).ConfigureAwait(false);
        }

        return staged;
    }

    /// <summary>
    ///     Finds an envelope already queued by this instance for the attempted message identifier or tenant-scoped
    ///     idempotency key.
    /// </summary>
    /// <param name="envelope">The envelope created for the current enqueue attempt.</param>
    /// <returns>The queued envelope, or <see langword="null" /> when this instance queued nothing matching.</returns>
    private OutboxEnvelope? FindQueuedEnvelope(OutboxEnvelope envelope)
    {
        if (_queuedByMessageId.TryGetValue(envelope.Id, out var byMessageId))
        {
            return byMessageId;
        }

        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey) &&
            _queuedByIdempotencyScope.TryGetValue(
                DurableIdempotencyScope.CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey),
                out var byScope))
        {
            return byScope;
        }

        return null;
    }

    /// <summary>
    ///     Tracks a queued envelope by message identifier and tenant-scoped idempotency key.
    /// </summary>
    /// <param name="envelope">The envelope queued on the document session.</param>
    private void TrackQueuedEnvelope(OutboxEnvelope envelope)
    {
        _queuedByMessageId[envelope.Id] = envelope;

        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            _queuedByIdempotencyScope[DurableIdempotencyScope.CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey)] =
                envelope;
        }
    }

    /// <summary>
    ///     Throws when a strict submission differs from the envelope already stored or queued for its identifier.
    /// </summary>
    /// <param name="envelope">The envelope created for the current enqueue attempt.</param>
    /// <param name="existing">The stored or queued envelope that caused the conflict.</param>
    private static void ThrowIfStrictConflict(OutboxEnvelope envelope, OutboxEnvelope existing)
    {
        if (envelope.IdempotencyConflictMode == IdempotencyConflictMode.Strict &&
            !PostgreSqlOutboxStore.HasSameSubmission(envelope, existing))
        {
            throw new IdempotencyConflictException(
                $"An outbox message with idempotency key '{envelope.IdempotencyKey}' or message id '{envelope.Id}' already exists.");
        }
    }
}
