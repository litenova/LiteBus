using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Records terminal inbox execution outcomes and supports dead-letter replay.
/// </summary>
/// <remarks>
///     <see cref="IInboxProcessor" /> depends on this role. Retention and diagnostics are exposed through separate
///     interfaces so hosts can grant processors only the capabilities they need.
/// </remarks>
public interface IInboxTerminalStateStore
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
    ///     Moves multiple envelopes to the dead-letter state in one store round trip.
    /// </summary>
    /// <param name="deadLetters">The dead-letter details for each envelope.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch status update.</returns>
    Task MoveToDeadLetterAsync(IReadOnlyList<InboxEnvelopeDeadLetter> deadLetters, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves a dead-lettered envelope back to the pending state for manual replay.
    /// </summary>
    /// <param name="messageId">The envelope identifier to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous requeue operation.</returns>
    Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves multiple dead-lettered envelopes back to the pending state for manual replay.
    /// </summary>
    /// <param name="messageIds">The envelope identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the status update.</param>
    /// <returns>A task that represents the asynchronous batch requeue operation.</returns>
    Task RequeueDeadLetterAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Moves multiple dead-lettered envelopes back to the pending state for manual replay using string message identifiers.
    /// </summary>
    /// <param name="messageIds">The envelope identifiers to requeue. Each value must parse as a <see cref="Guid" />.</param>
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
