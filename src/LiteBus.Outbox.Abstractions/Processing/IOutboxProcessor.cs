using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Leases due outbox messages and publishes them through an outbox dispatcher.
/// </summary>
/// <remarks>
///     <para>
///         Host this processor from a worker, timer, hosted service, or manual maintenance job. Each call performs one
///         processing pass: lease a batch, dispatch each message, and record published, retry, or dead-letter state.
///     </para>
///     <para>
///         Publication is at least once. Dispatchers and downstream consumers should handle duplicate messages by using
///         the outbox message id or a business key.
///     </para>
/// </remarks>
public interface IOutboxProcessor
{
    /// <summary>
    ///     Processes one batch of due outbox messages.
    /// </summary>
    /// <param name="cancellationToken">A token used to stop leasing or to stop before the next message is dispatched.</param>
    Task ProcessPendingAsync(CancellationToken cancellationToken = default);
}