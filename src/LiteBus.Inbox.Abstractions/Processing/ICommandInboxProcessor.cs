using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Leases due inbox commands and executes them through the command mediator.
/// </summary>
/// <remarks>
///     <para>
///         Host this processor from a worker, timer, hosted service, or manual maintenance job. Each call performs one
///         processing pass: lease a batch, execute each command, and record completion, retry, or dead-letter state.
///     </para>
///     <para>
///         Processing is at least once. Command handlers should be idempotent around external side effects and database
///         writes that can be retried.
///     </para>
/// </remarks>
public interface ICommandInboxProcessor
{
    /// <summary>
    ///     Processes one batch of due inbox commands.
    /// </summary>
    /// <param name="cancellationToken">A token used to stop leasing or to stop before the next command is executed.</param>
    /// <returns>A pass result that reports how many commands were leased and processed in this pass.</returns>
    Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default);
}