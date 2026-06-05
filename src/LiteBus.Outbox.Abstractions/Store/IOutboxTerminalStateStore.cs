using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Records terminal publication outcomes and supports dead-letter replay for outbox messages.
/// </summary>
/// <remarks>
///     <see cref="IOutboxProcessor" /> depends on this role. Retention and diagnostics are exposed through separate
///     interfaces so hosts can grant processors only the capabilities they need.
/// </remarks>
public interface IOutboxTerminalStateStore
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
    ///     Moves multiple messages to the dead-letter state in one store round trip.
    /// </summary>
    /// <param name="deadLetters">The dead-letter details for each message.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch status update.</returns>
    Task MoveToDeadLetterAsync(IReadOnlyList<OutboxEnvelopeDeadLetter> deadLetters, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a dead-lettered message back to the pending state for manual replay.
    /// </summary>
    /// <param name="messageId">The message identifier to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous requeue operation.</returns>
    Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves multiple dead-lettered messages back to the pending state for manual replay.
    /// </summary>
    /// <param name="messageIds">The message identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch requeue operation.</returns>
    Task RequeueDeadLetterAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves multiple dead-lettered messages back to the pending state for manual replay using string message identifiers.
    /// </summary>
    /// <param name="messageIds">The message identifiers to requeue. Each value must parse as a <see cref="Guid" />.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch requeue operation.</returns>
    Task RequeueDeadLetterAsync(IReadOnlyList<string> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        var parsedIds = new Guid[messageIds.Count];

        for (var index = 0; index < messageIds.Count; index++)
        {
            if (!Guid.TryParse(messageIds[index], out parsedIds[index]))
            {
                throw new ArgumentException(
                    $"Message id '{messageIds[index]}' is not a valid GUID.",
                    nameof(messageIds));
            }
        }

        return RequeueDeadLetterAsync(parsedIds, cancellationToken);
    }
}
