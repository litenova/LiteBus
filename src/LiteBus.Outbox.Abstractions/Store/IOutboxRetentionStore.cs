using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Deletes published outbox messages that are older than a retention cutoff.
/// </summary>
/// <remarks>
///     Cleanup background services depend on this role without requiring terminal state transition capabilities.
/// </remarks>
public interface IOutboxRetentionStore
{
    /// <summary>
    ///     Deletes published messages whose terminal timestamp is older than the supplied cutoff.
    /// </summary>
    /// <param name="olderThan">
    ///     Rows with <c>COALESCE(published_at, created_at)</c> strictly before this timestamp are eligible for deletion.
    ///     When <c>published_at</c> is set, retention uses publication time rather than enqueue time.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}