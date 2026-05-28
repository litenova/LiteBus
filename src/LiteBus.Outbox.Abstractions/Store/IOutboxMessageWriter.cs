using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Persists newly accepted outbox messages before a processor attempts publication.
/// </summary>
/// <remarks>
///     <para>
///         Implement this role in the same transaction boundary as the application state change that produced the
///         event. The writer is append-oriented: it accepts an immutable <see cref="OutboxMessageEnvelope" /> and
///         returns the row that became the durable source of truth. Stores should treat duplicate message identifiers
///         as idempotent inserts and return the existing row when the backing database can prove that the message was
///         already accepted.
///     </para>
///     <para>
///         This interface is intentionally separate from leasing and state transition APIs. Application code that only
///         writes messages should not need permission to lease or complete publication work.
///     </para>
/// </remarks>
public interface IOutboxMessageWriter
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
    ///     The stored envelope. For idempotent duplicate inserts, the returned value should be the original stored row
    ///     rather than a copy of the rejected input.
    /// </returns>
    Task<OutboxMessageEnvelope> AddAsync(OutboxMessageEnvelope envelope, CancellationToken cancellationToken = default);
}