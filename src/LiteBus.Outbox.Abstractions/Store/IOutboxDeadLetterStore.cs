using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Moves dead-lettered outbox messages back to the pending state for manual replay.
/// </summary>
public interface IOutboxDeadLetterStore
{
    /// <summary>
    ///     Requeues multiple dead-lettered messages for manual replay.
    /// </summary>
    /// <param name="messageIds">The message identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the requeue operation.</param>
    /// <returns>The number of rows requested versus actually requeued.</returns>
    Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues one dead-lettered message for manual replay.
    /// </summary>
    /// <param name="messageId">The message identifier to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the requeue operation.</param>
    /// <returns>The number of rows requested versus actually requeued.</returns>
    Task<RequeueResult> RequeueAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return RequeueAsync([messageId], cancellationToken);
    }
}
