using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Extension methods for <see cref="IInboxStore" /> that use enqueue-oriented naming.
/// </summary>
public static class InboxStoreExtensions
{
    /// <summary>
    ///     Enqueues a pending inbox envelope through the writer store role.
    /// </summary>
    /// <param name="store">The inbox writer store.</param>
    /// <param name="envelope">The envelope to persist.</param>
    /// <param name="cancellationToken">A token used to cancel the store write.</param>
    /// <returns>The stored envelope, or the existing envelope when the store detects a duplicate submission.</returns>
    public static Task<InboxEnvelope> EnqueueAsync(
        this IInboxStore store,
        InboxEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        return store.AddAsync(envelope, cancellationToken);
    }
}