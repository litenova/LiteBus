using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Deletes stored outbox messages that match operator-supplied predicates.
/// </summary>
public interface IOutboxPurgeStore
{
    /// <summary>
    ///     Deletes outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The predicates that select rows to delete.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default);
}
