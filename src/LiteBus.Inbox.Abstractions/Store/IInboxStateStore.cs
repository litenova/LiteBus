using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Records the execution result for a leased inbox envelope.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IInboxProcessor" /> uses this role after <see cref="IInboxDispatcher.DispatchAsync" /> completes
///         or throws. Implementations should clear lease metadata when recording completion, failure, or dead-letter
///         state. Failed envelopes should become visible according to the retry timestamp supplied by the processor.
///     </para>
///     <para>
///         This interface is separate from acceptance and leasing so custom stores can expose only the state transition
///         capability to processors.
///     </para>
/// </remarks>
public interface IInboxStateStore
{
    /// <summary>
    ///     Marks a leased envelope as completed after dispatch succeeds without throwing.
    /// </summary>
    /// <param name="messageId">The envelope identifier.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkCompletedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a leased envelope as failed and records when the next execution attempt may occur.
    /// </summary>
    /// <param name="failure">The failure details, including the envelope id, error text, and next visibility time.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves an envelope to the dead-letter state after retry attempts are exhausted.
    /// </summary>
    /// <param name="deadLetter">The dead-letter details, including the envelope id and diagnostic reason.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks multiple leased envelopes as completed after dispatch succeeds.
    /// </summary>
    /// <param name="messageIds">The envelope identifiers completed during one processor pass.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch status update.</returns>
    Task MarkCompletedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks multiple leased envelopes as failed and records their next visibility times.
    /// </summary>
    /// <param name="failures">The failure details for each envelope.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch status update.</returns>
    Task MarkFailedAsync(IReadOnlyList<InboxEnvelopeFailure> failures, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a dead-lettered envelope back to the pending state for manual replay.
    /// </summary>
    /// <param name="messageId">The envelope identifier to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous requeue operation.</returns>
    Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes completed envelopes whose completion time is older than the supplied cutoff.
    /// </summary>
    /// <param name="olderThan">Rows with <c>created_at</c> strictly before this timestamp are eligible for deletion.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the number of stored envelopes grouped by <see cref="InboxStatus" />.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A read-only map of status to row count.</returns>
    Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default);
}