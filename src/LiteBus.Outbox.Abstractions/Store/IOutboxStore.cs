using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Persists newly accepted outbox messages before a processor attempts publication.
/// </summary>
/// <remarks>
///     <para>
///         Implement this role in the same transaction boundary as the application state change that produced the
///         event. The writer is append-oriented: it accepts an immutable <see cref="OutboxEnvelope" /> and
///         returns the row that became the source of truth. Stores should treat duplicate message identifiers
///         as idempotent inserts and return the existing row when the backing database can prove that the message was
///         already accepted.
///     </para>
///     <para>
///         This interface is intentionally separate from leasing and state transition APIs. Application code that only
///         writes messages should not need permission to lease or complete publication work.
///     </para>
/// </remarks>
public interface IOutboxStore
{
    /// <summary>
    ///     Adds a pending message envelope to the outbox.
    /// </summary>
    /// <param name="envelope">
    ///     The already serialized message envelope. The caller is responsible for assigning the message identifier,
    ///     stable contract name, contract version, payload, metadata, and initial status.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the database write before it is committed.</param>
    /// <returns>
    ///     The stored envelope and whether this append inserted it or resolved an existing idempotent submission.
    /// </returns>
    Task<OutboxAppendResult> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds multiple pending outbox envelopes in one store round trip.
    /// </summary>
    /// <param name="envelopes">
    ///     The serialized envelopes with identifiers, stable contracts, payloads, metadata, and initial status already
    ///     assigned.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the store write before it is committed.</param>
    /// <returns>
    ///     The append results in the same order as <paramref name="envelopes" />. Each result identifies whether its
    ///     envelope was inserted or resolved from a duplicate idempotency key or message identifier.
    /// </returns>
    Task<IReadOnlyList<OutboxAppendResult>> AddBatchAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken = default);
}
