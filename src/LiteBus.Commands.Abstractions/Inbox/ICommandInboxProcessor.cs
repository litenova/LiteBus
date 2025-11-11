using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Defines the contract for a component that processes commands from a durable inbox.
///     Implementations are responsible for the entire processing lifecycle, including fetching
///     commands in batches, ensuring fault-tolerance, and handling retries.
/// </summary>
public interface ICommandInboxProcessor
{
    /// <summary>
    ///     Runs the long-running processor. The returned <see cref="Task" /> represents the entire
    ///     lifetime of the processor and will only complete when processing has stopped, either
    ///     due to cancellation or a fatal, unrecoverable error.
    /// </summary>
    /// <param name="handler">The delegate that will be invoked with each batch of commands.</param>
    /// <param name="cancellationToken">A token to signal that the processor should stop its work.</param>
    /// <returns>A <see cref="Task" /> that represents the background processing operation.</returns>
    Task RunAsync(CommandBatchHandler handler, CancellationToken cancellationToken = default);
}