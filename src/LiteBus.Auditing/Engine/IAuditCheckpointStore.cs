using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Reads stored audit checkpoints back without a document type.
/// </summary>
/// <remarks>
///     The chain facade verifies from a trusted checkpoint and lists unpublished ones, and neither needs the
///     stored document type, so this contract keeps the rest of the chain store port out of the facade.
/// </remarks>
internal interface IAuditCheckpointStore
{
    /// <summary>
    ///     Reads one stored checkpoint by identity.
    /// </summary>
    /// <param name="checkpointId">The checkpoint identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The checkpoint, or <see langword="null" /> when no checkpoint carries that identity.</returns>
    ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken);

    /// <summary>
    ///     Lists the stored checkpoints still waiting for publication.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the listing.</param>
    /// <returns>The checkpoints whose publish state is <see cref="CheckpointPublishState.Pending" />.</returns>
    IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(CancellationToken cancellationToken);
}
