using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Operator-facing inbox browse, replay, purge, and diagnostics API.
/// </summary>
public interface IInboxManager
{
    /// <summary>
    ///     Returns one page of stored inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <param name="pageRequest">The page size and continuation cursor.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A page of matching envelopes and an optional cursor for the next page.</returns>
    Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues every dead-lettered inbox message back to the pending state.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The predicates that select rows to delete.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the number of stored envelopes grouped by <see cref="InboxStatus" />.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A read-only map of status to row count.</returns>
    Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default);
}
