using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Extension methods for <see cref="IOutboxStore" /> that use enqueue-oriented naming.
/// </summary>
public static class OutboxStoreExtensions
{
    /// <summary>
    ///     Enqueues a pending outbox envelope through the writer store role.
    /// </summary>
    /// <param name="store">The outbox writer store.</param>
    /// <param name="envelope">The envelope to persist.</param>
    /// <param name="cancellationToken">A token used to cancel the store write.</param>
    /// <returns>The stored envelope, or the existing envelope when the store detects a duplicate submission.</returns>
    public static Task<OutboxEnvelope> EnqueueAsync(
        this IOutboxStore store,
        OutboxEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        return store.AddAsync(envelope, cancellationToken);
    }
}