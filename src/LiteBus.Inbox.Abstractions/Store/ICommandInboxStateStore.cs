using System;
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
    /// <param name="commandId">The envelope identifier.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default);

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
}