using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Extension methods for <see cref="IInboxDeadLetterStore" />.
/// </summary>
public static class InboxDeadLetterStoreExtensions
{
    /// <summary>
    ///     Requeues multiple dead-lettered envelopes using string message identifiers.
    /// </summary>
    /// <param name="store">The dead-letter store that performs the requeue operation.</param>
    /// <param name="messageIds">The envelope identifiers to requeue. Each value must parse as a <see cref="Guid" />.</param>
    /// <param name="cancellationToken">A token that cancels the requeue operation.</param>
    /// <returns>A task that completes with the number of messages requested and requeued.</returns>
    public static Task<RequeueResult> RequeueAsync(
        this IInboxDeadLetterStore store,
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return Task.FromResult(new RequeueResult(0, 0));
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

        return store.RequeueAsync(parsedIds, cancellationToken);
    }
}