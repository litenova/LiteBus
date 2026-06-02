using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Signals when startup-phase background services have finished so long-running loops can begin.
/// </summary>
internal sealed class BackgroundServiceStartupGate
{
    /// <summary>
    ///     The task that completes when startup-phase background services finish.
    /// </summary>
    private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Returns a task that completes when <see cref="SignalComplete" /> is called.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>A task that completes when startup work finishes.</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        return _completionSource.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Marks startup-phase background services as complete so continuous services can start.
    /// </summary>
    public void SignalComplete()
    {
        _completionSource.TrySetResult();
    }
}
