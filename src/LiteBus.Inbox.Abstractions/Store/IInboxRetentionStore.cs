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
    ///     Deletes completed envelopes whose completion time is older than the supplied cutoff.
    /// </summary>
    /// <param name="olderThan">Rows with <c>created_at</c> strictly before this timestamp are eligible for deletion.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
