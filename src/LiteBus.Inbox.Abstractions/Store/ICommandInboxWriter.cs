using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Persists messages that have been accepted for later execution.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IInbox" /> uses this role after it resolves the stable message contract and serializes the payload.
///         Store implementations should write the envelope in the same transaction boundary as the caller's acceptance
///         logic when that boundary exists. Duplicate idempotency keys should return the previously accepted envelope
///         rather than creating a second row.
///     </para>
///     <para>
///         This interface is append-oriented. Acceptance code should not depend on leasing or completion APIs.
///     </para>
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    ///     Adds a pending inbox envelope.
    /// </summary>
    /// <param name="envelope">
    ///     The serialized envelope with its identifier, stable contract, payload, visibility timestamp, idempotency key,
    ///     and tracing metadata already assigned.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the store write before it is committed.</param>
    /// <returns>The stored envelope, or the existing envelope when the store detects a duplicate submission.</returns>
    Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default);
}