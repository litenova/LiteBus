using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Deletes stored inbox messages that match operator-supplied predicates.
/// </summary>
public interface IInboxPurgeStore
{
    /// <summary>
    ///     Deletes inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The predicates that select rows to delete.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default);
}
