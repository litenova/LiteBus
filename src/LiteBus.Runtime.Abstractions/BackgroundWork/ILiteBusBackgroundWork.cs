using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines long-running or startup work that LiteBus registers with the application host through a container adapter.
/// </summary>
public interface ILiteBusBackgroundWork
{
    /// <summary>
    ///     Runs until <paramref name="cancellationToken" /> is canceled.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop the work.</param>
    /// <returns>A task that completes when the work stops.</returns>
    Task RunAsync(CancellationToken cancellationToken);
}
