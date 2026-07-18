using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Exposes aggregate inbox queue diagnostics for operators and tooling.
/// </summary>
public interface IInboxDiagnosticsStore
{
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
}