using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;

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
    ///     Returns one stored inbox message by identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching envelope, or <see langword="null" /> when no row exists.</returns>
    Task<InboxEnvelope?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues every dead-lettered inbox message back to the pending state.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues the supplied inbox message identifiers back to the pending state.
    /// </summary>
    /// <param name="messageIds">The envelope identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    Task<int> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The predicates that select rows to delete.</param>
    /// <param name="confirm">
    ///     When <see langword="true" />, allows deleting every row when the filter is unrestricted.
    ///     Otherwise at least one narrowing predicate is required.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    /// <exception cref="InboxManagementException">
    ///     Thrown when <paramref name="confirm" /> is <see langword="false" /> and the filter matches all rows.
    /// </exception>
    Task<int> PurgeAsync(
        InboxMessageFilter filter,
        bool confirm = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the number of stored envelopes grouped by <see cref="InboxStatus" />.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A read-only map of status to row count.</returns>
    Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns schema version metadata for the configured inbox store.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>Expected and recorded schema versions for the active store backend.</returns>
    Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns retention cleanup status for the inbox axis.
    /// </summary>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>The configured retention policy and most recent cleanup outcome.</returns>
    Task<RetentionRunStatus> GetRetentionStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes completed inbox messages older than the configured retention period immediately.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> RunRetentionPurgeAsync(CancellationToken cancellationToken = default);
}