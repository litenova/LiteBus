using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Queries stored outbox messages with optional filters and keyset pagination.
/// </summary>
public interface IOutboxMessageQuery
{
    /// <summary>
    ///     Returns one page of outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <param name="pageRequest">The page size and continuation cursor.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A page of matching envelopes and an optional cursor for the next page.</returns>
    Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default);
}