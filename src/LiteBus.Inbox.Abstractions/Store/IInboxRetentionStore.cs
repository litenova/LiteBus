using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Deletes completed inbox envelopes that are older than a retention cutoff.
/// </summary>
public interface IInboxRetentionStore
{
    /// <summary>
    ///     Deletes completed envelopes whose terminal timestamp is older than the supplied cutoff.
    /// </summary>
    /// <param name="olderThan">
    ///     Rows with <c>COALESCE(completed_at, created_at)</c> strictly before this timestamp are eligible for deletion.
    ///     When <c>completed_at</c> is set, retention uses completion time rather than acceptance time.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
