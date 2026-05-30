using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Leases due inbox envelopes and dispatches them through <see cref="IInboxDispatcher" />.
/// </summary>
/// <remarks>
///     <para>
///         Host this processor from a worker, timer, hosted service, or manual maintenance job. Each call performs one
///         processing pass: lease a batch, dispatch each envelope, and record completion, retry, or dead-letter state.
///     </para>
///     <para>
///         Processing is at least once. Dispatch targets and handlers should be idempotent around external side effects
///         and database writes that can be retried.
///     </para>
/// </remarks>
public interface IInboxProcessor
{
    /// <summary>
    ///     Processes one batch of due inbox envelopes.
    /// </summary>
    /// <param name="cancellationToken">A token used to stop leasing or to stop before the next envelope is dispatched.</param>
    /// <returns>A pass result that reports how many envelopes were leased in this pass.</returns>
    Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default);
}