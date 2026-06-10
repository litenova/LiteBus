using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Operator-facing outbox browse, replay, purge, and diagnostics API.
/// </summary>
public interface IOutboxManager
{
    /// <summary>
    ///     Returns one page of stored outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <param name="pageRequest">The page size and continuation cursor.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A page of matching envelopes and an optional cursor for the next page.</returns>
    Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns one stored outbox message by identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching envelope, or <see langword="null" /> when no row exists.</returns>
    Task<OutboxEnvelope?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues every dead-lettered outbox message back to the pending state.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requeues the supplied outbox message identifiers back to the pending state.
    /// </summary>
    /// <param name="messageIds">The message identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    Task<int> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="filter">The predicates that select rows to delete.</param>
    /// <param name="confirm">
    ///     When <see langword="true" />, allows deleting every row when the filter is unrestricted.
    ///     Otherwise at least one narrowing predicate is required.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    /// <exception cref="OutboxManagementException">
    ///     Thrown when <paramref name="confirm" /> is <see langword="false" /> and the filter matches all rows.
    /// </exception>
    Task<int> PurgeAsync(
        OutboxMessageFilter filter,
        bool confirm = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the number of stored messages grouped by <see cref="OutboxStatus" />.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A read-only map of status to row count.</returns>
    Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns schema version metadata for the configured outbox store.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>Expected and recorded schema versions for the active store backend.</returns>
    Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns retention cleanup status for the outbox axis.
    /// </summary>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>The configured retention policy and most recent cleanup outcome.</returns>
    Task<RetentionRunStatus> GetRetentionStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes published outbox messages older than the configured retention period immediately.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> RunRetentionPurgeAsync(CancellationToken cancellationToken = default);
}
