using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using LiteBus.Messaging.Abstractions;
using Marten;
using Marten.Exceptions;
using Npgsql;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Writes audit records as Marten documents, staged on the scoped session for successes and out of band otherwise.
/// </summary>
/// <remarks>
///     <para>
///         A record for a successful mediation is staged on the scoped <see cref="IDocumentSession" /> and committed by
///         the same <c>SaveChangesAsync</c> that commits the handler change, so the record and the change are atomic:
///         both commit together and both roll back together. The ordering that makes this work is fixed by priority.
///         The audit completion handler runs at <see cref="HandlerPriorities.Observability" /> and the unit-of-work
///         commit runs at <see cref="HandlerPriorities.UnitOfWork" />, and handlers run in ascending priority order,
///         so the writer always stages before the commit.
///     </para>
///     <para>
///         A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the
///         unit of work rolls the session back on exactly those paths. Those records are written out of band on a
///         short session opened from <see cref="IDocumentStore" /> and saved immediately, so a refusal or a fault is
///         still recorded when the change it describes never commits.
///     </para>
///     <para>
///         Registered with the existing messaging <c>UseAuditTrail</c> API, which constructs this trail from the
///         container with its default scoped lifetime. The application registers Marten itself with <c>AddMarten</c>,
///         which supplies both the scoped session and the document store; LiteBus does not register Marten.
///     </para>
///     <para>
///         When <see cref="MartenAuditTrailOptions" /> enables the hash chain, every record, including successes, is
///         written out of band on its own serialized session instead. The sequence number is assigned under the
///         segment lock at commit time, which a staged session cannot hold: the unit of work commits later, on a
///         session the trail does not control. A chained success record is therefore durable on its own rather than
///         atomic with the handler change, and a rolled-back handler still leaves its audit record behind.
///     </para>
/// </remarks>
public sealed class MartenAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the scoped document session that carries staged success records until the unit of work commits.
    /// </summary>
    private readonly IDocumentSession _session;

    /// <summary>
    ///     Gets the document store that opens short sessions for out-of-band non-success records.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Gets the hash chain, segmenting, and checkpoint configuration.
    /// </summary>
    private readonly MartenAuditTrailOptions _options;

    /// <summary>
    ///     Gets the clock that stamps published checkpoints.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditTrail" /> class.
    /// </summary>
    /// <param name="session">The scoped document session the handler staged its documents on.</param>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="options">
    ///     The hash chain configuration, or <see langword="null" /> to write plain records with no chain.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock that stamps published checkpoints, or <see langword="null" /> for
    ///     <see cref="TimeProvider.System" />.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="options" /> carries a checkpoint interval that is zero or negative.
    /// </exception>
    public MartenAuditTrail(
        IDocumentSession session,
        IDocumentStore documentStore,
        MartenAuditTrailOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(documentStore);

        options ??= new MartenAuditTrailOptions();
        options.Validate();

        _session = session;
        _documentStore = documentStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_options.HashChainEnabled)
        {
            await WriteChainedAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        var document = Map(record);

        if (record.Outcome == AuditOutcome.Succeeded)
        {
            // Staged, not saved: the unit-of-work completion handler runs after the audit writer and commits this
            // session, so the record commits in the same SaveChangesAsync as the handler change.
            _session.Store(document);
            return;
        }

        // The unit of work rolls the handler session back on this path, so staging here would lose the record. A
        // separate short session saves it immediately instead.
        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            session.Store(document);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies the hash chain of one segment from its first record to its head.
    /// </summary>
    /// <param name="segment">The segment to verify, as the configured segment selector named it.</param>
    /// <param name="cancellationToken">The token used to cancel the verification read.</param>
    /// <returns>
    ///     The first broken sequence number: a missing sequence, a record whose predecessor link does not match the
    ///     hash of the record before it, or a record whose stored hash does not match its recomputed hash. Returns
    ///     <see langword="null" /> when the chain is intact, including when the segment holds no records.
    /// </returns>
    /// <remarks>
    ///     Verification proves internal consistency only: that nothing was removed, reordered, or altered since the
    ///     records were written. It cannot prove that a whole suffix was not rewritten with recomputed hashes; that
    ///     is what published checkpoints anchor. Compare the head hash against the latest checkpoint the
    ///     <see cref="ICheckpointPublisher" /> stored for the segment to close that gap.
    /// </remarks>
    public async Task<long?> VerifyChainAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var records = await session
                .Query<MartenAuditRecord>()
                .Where(record => record.Segment == segment)
                .OrderBy(record => record.Sequence)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return FindFirstBrokenSequence(records);
        }
    }

    /// <summary>
    ///     Appends one record to its segment with the next sequence number and a hash over its predecessor.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the append.</param>
    /// <returns>A task representing the asynchronous append.</returns>
    /// <remarks>
    ///     A lost append race retries against the winner: a concurrent append fails with an optimistic concurrency
    ///     conflict on the segment head, and a head creation race fails with a unique violation on the head identity.
    ///     The loop ends when a session commits, and the cancellation check keeps a cancelled mediation from
    ///     appending forever under contention.
    /// </remarks>
    private async Task WriteChainedAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var segment = _options.SegmentOf(record);

        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new InvalidOperationException(
                $"The audit segment selector returned no segment for action '{record.Action}'. " +
                "Return a stable segment name from the selector passed to UseHashChain.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

                await using (session.ConfigureAwait(false))
                {
                    var head = await session
                        .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                        .ConfigureAwait(false);

                    var sequence = (head?.LastSequence ?? 0) + 1;
                    var document = Map(record);
                    document.Segment = segment;
                    document.Sequence = sequence;
                    document.PreviousHash = head?.LastHash;
                    document.Hash = MartenAuditHashChain.ComputeHash(document);

                    // The loaded head is updated in place, never replaced: the update asserts the version captured
                    // at load, which is the check that serializes concurrent appends.
                    head ??= new MartenAuditSegmentHead { Id = segment };
                    head.LastSequence = sequence;
                    head.LastHash = document.Hash;

                    session.Store(document);
                    session.Store(head);

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await PublishCheckpointWhenDueAsync(segment, sequence, document.Hash, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception exception) when (IsAppendConflict(exception))
            {
                // Another writer committed first; the next pass reads the winner.
            }
        }
    }

    /// <summary>
    ///     Publishes a checkpoint when an appended record closes a checkpoint interval.
    /// </summary>
    /// <param name="segment">The segment the record was appended to.</param>
    /// <param name="sequence">The sequence number assigned to the record.</param>
    /// <param name="hash">The chain hash of the record.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns>A task representing the asynchronous publish.</returns>
    private async Task PublishCheckpointWhenDueAsync(
        string segment,
        long sequence,
        string hash,
        CancellationToken cancellationToken)
    {
        var publisher = _options.CheckpointPublisher;

        if (publisher is null || sequence % _options.CheckpointInterval != 0)
        {
            return;
        }

        await publisher
            .PublishAsync(new AuditCheckpoint(segment, sequence, hash, _timeProvider.GetUtcNow()), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Finds the first broken sequence number in segment order.
    /// </summary>
    /// <param name="records">The segment records in ascending sequence order.</param>
    /// <returns>
    ///     The first missing sequence, the sequence of the first record with a broken predecessor link or hash, or
    ///     <see langword="null" /> when every record links and hashes correctly.
    /// </returns>
    private static long? FindFirstBrokenSequence(IReadOnlyList<MartenAuditRecord> records)
    {
        var expected = 1L;
        string? previousHash = null;

        foreach (var record in records)
        {
            // A record past the expected sequence leaves a gap: the missing sequence is the broken one. A record at
            // or below it is a duplicate or a replay: the record itself is the broken one.
            if (record.Sequence != expected)
            {
                return record.Sequence < expected ? record.Sequence : expected;
            }

            if (!string.Equals(record.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return record.Sequence;
            }

            if (!string.Equals(MartenAuditHashChain.ComputeHash(record), record.Hash, StringComparison.Ordinal))
            {
                return record.Sequence;
            }

            previousHash = record.Hash;
            expected++;
        }

        return null;
    }

    /// <summary>
    ///     Decides whether a failure an append session raised is a lost append race worth retrying.
    /// </summary>
    /// <param name="exception">The failure an append session raised, whatever wrapper Marten put around it.</param>
    /// <returns><see langword="true" /> when the chain reports a concurrency or head creation conflict.</returns>
    private static bool IsAppendConflict(Exception exception)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            if (current is ConcurrencyException or ConcurrentUpdateException)
            {
                return true;
            }

            if (current is PostgresException postgres && IsHeadWriteState(postgres.SqlState))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    /// <remarks>
    ///     A batched commit can report several failures in one <see cref="AggregateException" />, and its
    ///     <see cref="Exception.InnerException" /> is only the first of them, so walking that chain alone can miss
    ///     the storage error that matters.
    /// </remarks>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }

    /// <summary>
    ///     Decides whether a PostgreSQL state reports a lost head write race.
    /// </summary>
    /// <param name="sqlState">The five-character state the storage error reported.</param>
    /// <returns>
    ///     <see langword="true" /> for a unique violation (<c>23505</c>), a serialization failure (<c>40001</c>), or a
    ///     deadlock (<c>40P01</c>); otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsHeadWriteState(string sqlState)
    {
        return sqlState is "23505" or "40001" or "40P01";
    }

    /// <summary>
    ///     Flattens one audit record into its Marten document.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <returns>The document staged or written for the record.</returns>
    private static MartenAuditRecord Map(AuditRecord record)
    {
        return new MartenAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = record.Action,
            ActorId = record.Actor?.Id,
            ActorKind = record.Actor?.Kind,
            ActorDisplayName = record.Actor?.DisplayName,
            ActorOnBehalfOf = record.Actor?.OnBehalfOf,
            Outcome = record.Outcome,
            OccurredAt = record.OccurredAt,
            Duration = record.Duration,
            Category = record.Category,
            TargetKind = record.TargetKind,
            TargetId = record.TargetId,
            Reason = record.Reason,
            FailureCode = record.FailureCode,
            MessageType = record.MessageType,
            CorrelationId = record.CorrelationId,
            TenantId = record.TenantId,
            Properties = record.Properties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal)
        };
    }
}
