using System.Collections.Generic;
using LiteBus.Auditing.Abstractions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Lists, expires, and erases audit chain segments.
/// </summary>
internal interface IAuditSegmentManager
{
    /// <summary>
    ///     Lists the segments matching a filter.
    /// </summary>
    /// <param name="filter">The segments to list.</param>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The matching segment summaries.</returns>
    IAsyncEnumerable<AuditSegmentSummary> ListAsync(
        AuditSegmentFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Publishes a final checkpoint for a segment, then deletes the segment's documents and closes it to new
    ///     writes.
    /// </summary>
    /// <param name="segment">The segment to expire.</param>
    /// <param name="cancellationToken">The token used to cancel the expiry.</param>
    /// <returns>The final checkpoint.</returns>
    ValueTask<AuditCheckpoint> ExpireAsync(string segment, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes the records, heads, and stored checkpoints matching a filter.
    /// </summary>
    /// <param name="filter">The segments to erase.</param>
    /// <param name="reason">Why the segments are erased.</param>
    /// <param name="cancellationToken">The token used to cancel the erasure.</param>
    /// <returns>The number of segments erased.</returns>
    ValueTask<int> EraseAsync(AuditSegmentFilter filter, string reason, CancellationToken cancellationToken);
}
