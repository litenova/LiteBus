using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Provides optional hooks that run when the command inbox processor host starts or stops.
/// </summary>
public interface ICommandInboxProcessorHostLifecycle
{
    /// <summary>
    ///     Runs before the first processing pass after any configured startup delay.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel startup work.</param>
    Task OnHostStartingAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Runs when the host loop is stopping.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel shutdown work.</param>
    Task OnHostStoppingAsync(CancellationToken cancellationToken);
}
