using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Assigns chain positions to records that committed unlinked.
/// </summary>
internal interface IAuditLinker
{
    /// <summary>
    ///     Links every pending record of a segment in commit order.
    /// </summary>
    /// <param name="segment">The segment to link.</param>
    /// <param name="cancellationToken">The token used to cancel the linking.</param>
    /// <returns>A task representing the asynchronous linking.</returns>
    ValueTask LinkPendingAsync(string segment, CancellationToken cancellationToken);

    /// <summary>
    ///     Links the records a crash left unlinked.
    /// </summary>
    /// <param name="olderThan">Only records staged longer ago than this are linked.</param>
    /// <param name="cancellationToken">The token used to cancel the sweep.</param>
    /// <returns>The number of records linked.</returns>
    ValueTask<int> SweepAsync(TimeSpan olderThan, CancellationToken cancellationToken);
}
