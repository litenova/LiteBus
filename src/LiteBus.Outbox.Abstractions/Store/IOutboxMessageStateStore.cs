using System;
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
public interface IOutboxMessageStateStore
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
    Task MarkFailedAsync(OutboxMessageFailure failure, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a message to the dead-letter state after retry attempts are exhausted or a processor chooses to stop retrying.
    /// </summary>
    /// <param name="deadLetter">The dead-letter details, including the message id and diagnostic reason.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MoveToDeadLetterAsync(OutboxMessageDeadLetter deadLetter, CancellationToken cancellationToken = default);
}