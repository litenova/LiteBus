using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Records the publication result for a leased outbox message.
/// </summary>
/// <remarks>
///     <para>
///         Processors use this role after a dispatcher returns or throws. Implementations should clear lease metadata
///         when a message reaches a terminal or retry state. A failed message should become visible according to the
///         retry timestamp supplied by the processor; a dead-lettered message should remain available for diagnostics
///         or manual replay tooling.
///     </para>
///     <para>
///         This interface does not expose append or lease operations. Keeping state transitions separate makes custom
///         stores easier to test and allows hosts to grant processors only the capabilities they need.
///     </para>
/// </remarks>
public interface IOutboxStateStore
{
    /// <summary>
    ///     Marks a leased message as published after the dispatcher has completed without throwing.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a leased message as failed and records when the next publication attempt may occur.
    /// </summary>
    /// <param name="failure">The failure details, including the message id, error text, and next visibility time.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkFailedAsync(OutboxEnvelopeFailure failure, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a message to the dead-letter state after retry attempts are exhausted or a processor chooses to stop retrying.
    /// </summary>
    /// <param name="deadLetter">The dead-letter details, including the message id and diagnostic reason.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MoveToDeadLetterAsync(OutboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks multiple leased messages as published after dispatch succeeds.
    /// </summary>
    /// <param name="messageIds">The message identifiers published during one processor pass.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch status update.</returns>
    Task MarkPublishedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks multiple leased messages as failed and records their next visibility times.
    /// </summary>
    /// <param name="failures">The failure details for each message.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch status update.</returns>
    Task MarkFailedAsync(IReadOnlyList<OutboxEnvelopeFailure> failures, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a dead-lettered message back to the pending state for manual replay.
    /// </summary>
    /// <param name="messageId">The message identifier to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous requeue operation.</returns>
    Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes published messages whose creation time is older than the supplied cutoff.
    /// </summary>
    /// <param name="olderThan">Rows with <c>created_at</c> strictly before this timestamp are eligible for deletion.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the number of stored messages grouped by <see cref="OutboxStatus" />.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A read-only map of status to row count.</returns>
    Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default);
}