using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Records the execution result for a leased inbox command.
/// </summary>
/// <remarks>
///     <para>
///         Command processors use this role after `ICommandMediator.SendAsync` completes or throws. Implementations
///         should clear lease metadata when recording completion, failure, or dead-letter state. Failed commands should
///         become visible according to the retry timestamp supplied by the processor.
///     </para>
///     <para>
///         This interface is separate from scheduling and leasing so custom stores can expose only the state transition
///         capability to command processors.
///     </para>
/// </remarks>
public interface ICommandInboxStateStore
{
    /// <summary>
    ///     Marks a leased command as completed after the command mediator has executed it without throwing.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a leased command as failed and records when the next execution attempt may occur.
    /// </summary>
    /// <param name="failure">The failure details, including the command id, error text, and next visibility time.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MarkFailedAsync(InboxCommandFailure failure, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a command to the dead-letter state after retry attempts are exhausted or a processor stops retrying it.
    /// </summary>
    /// <param name="deadLetter">The dead-letter details, including the command id and diagnostic reason.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous status update.</returns>
    Task MoveToDeadLetterAsync(InboxCommandDeadLetter deadLetter, CancellationToken cancellationToken = default);
}