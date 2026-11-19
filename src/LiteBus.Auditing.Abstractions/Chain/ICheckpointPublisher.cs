using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Publishes audit chain checkpoints to an independent store.
/// </summary>
/// <remarks>
///     <para>
///         Where the checkpoint goes is the application's decision: an append-only file store, object storage with a
///         retention lock, or a transparency log. Publish synchronously: a checkpoint the application never stored
///         anchors nothing.
///     </para>
///     <para>
///         A publisher that throws fails the operation it was called from, so a lost checkpoint surfaces instead of
///         passing silently.
///     </para>
/// </remarks>
public interface ICheckpointPublisher
{
    /// <summary>
    ///     Publishes one audit chain checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint the chain reached.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns>A task representing the asynchronous publish.</returns>
    ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default);
}
