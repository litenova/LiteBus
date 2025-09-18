using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
/// Defines a unified contract for a durable command store that supports both writing
/// commands and processing them via a processor.
/// </summary>
public interface ICommandInbox
{
    /// <summary>
    /// Persists a command for deferred, guaranteed processing.
    /// </summary>
    /// <param name="command">The command to be stored.</param>
    /// <param name="cancellationToken">A token to cancel the storage operation.</param>
    Task StoreAsync(ICommand command, CancellationToken cancellationToken = default);
}