using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Extension methods for <see cref="IOutboxDeadLetterStore" />.
/// </summary>
public static class OutboxDeadLetterStoreExtensions
{
    /// <summary>
    ///     Requeues multiple dead-lettered messages using string message identifiers.
    /// </summary>
    /// <param name="store">The dead-letter store that performs the requeue operation.</param>
    /// <param name="messageIds">The message identifiers to requeue. Each value must parse as a <see cref="Guid" />.</param>
    /// <param name="cancellationToken">A token that cancels the requeue operation.</param>
    /// <returns>A task that represents the asynchronous requeue operation.</returns>
    public static Task RequeueAsync(
        this IOutboxDeadLetterStore store,
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
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

        return store.RequeueAsync(parsedIds, cancellationToken);
    }
}