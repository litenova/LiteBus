using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Leases due messages and dispatches them through a durable processor pass.
/// </summary>
public interface IMessageProcessor
{
    /// <summary>
    ///     Processes one batch of due messages.
    /// </summary>
    /// <param name="cancellationToken">A token used to stop leasing or dispatch.</param>
    /// <returns>A pass result that reports how many messages were leased during the pass.</returns>
    Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default);
}