using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Runs the command inbox processor in a continuous loop until cancellation is requested.
/// </summary>
/// <remarks>
///     <para>
///         This contract is DI-neutral. Microsoft hosting adapters wrap it with <see cref="Microsoft.Extensions.Hosting.IHostedService" />.
///         Applications
///         can also call <see cref="RunAsync" /> from their own worker infrastructure.
///     </para>
/// </remarks>
public interface ICommandInboxProcessorHost
{
    /// <summary>
    ///     Runs processing passes until <paramref name="cancellationToken" /> is canceled.
    /// </summary>
    /// <param name="cancellationToken">A token used to stop the loop between passes.</param>
    Task RunAsync(CancellationToken cancellationToken);
}
