using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Persists commands that have been accepted for later execution.
/// </summary>
/// <remarks>
///     <para>
///         The scheduler uses this role after it resolves the stable command contract and serializes the command. Store
///         implementations should write the envelope in the same transaction boundary as the caller's acceptance logic
///         when that boundary exists. Duplicate idempotency keys should return the previously accepted envelope rather
///         than creating a second command.
///     </para>
///     <para>
///         This interface is append-oriented. Scheduling code should not depend on leasing or completion APIs.
///     </para>
/// </remarks>
public interface ICommandInboxWriter
{
    /// <summary>
    ///     Adds a pending command envelope to the inbox.
    /// </summary>
    /// <param name="envelope">
    ///     The serialized command envelope with its identifier, stable contract, payload, visibility timestamp,
    ///     idempotency key, and tracing metadata already assigned.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the database write before it is committed.</param>
    /// <returns>The stored envelope, or the existing envelope when the store detects a duplicate submission.</returns>
    Task<InboxCommandEnvelope> AddAsync(InboxCommandEnvelope envelope, CancellationToken cancellationToken = default);
}