using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Moves dead-lettered inbox envelopes back to the pending state for manual replay.
/// </summary>
public interface IInboxDeadLetterStore
{
    /// <summary>
    ///     Requeues multiple dead-lettered envelopes for manual replay.
    /// </summary>
    /// <param name="messageIds">The envelope identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the requeue operation.</param>
    /// <returns>The number of rows requested versus actually requeued.</returns>
    Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues one dead-lettered envelope for manual replay.
    /// </summary>
    /// <param name="messageId">The envelope identifier to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the requeue operation.</param>
    /// <returns>The number of rows requested versus actually requeued.</returns>
    Task<RequeueResult> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return RequeueAsync([messageId], cancellationToken);
    }
}
