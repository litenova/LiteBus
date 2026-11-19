using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Reads back the latest checkpoint of a segment, so verification can compare the chain against it.
/// </summary>
public interface ICheckpointSource
{
    /// <summary>
    ///     Returns the latest checkpoint cut for a segment.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The latest checkpoint, or <see langword="null" /> when none was cut.</returns>
    ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken);
}
