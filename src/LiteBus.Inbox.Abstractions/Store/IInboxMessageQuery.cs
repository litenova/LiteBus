using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Queries stored inbox messages with optional filters and keyset pagination.
/// </summary>
public interface IInboxMessageQuery
{
    /// <summary>
    ///     Returns one page of inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <param name="pageRequest">The page size and continuation cursor.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A page of matching envelopes and an optional cursor for the next page.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    ///     The requested page size is less than 1 or greater than <see cref="InboxMessagePageRequest.MaxPageSize" />.
    /// </exception>
    Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default);
}
