using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Signals when startup tasks have finished so long-running background service loops can begin.
/// </summary>
internal sealed class StartupTaskGate
{
    /// <summary>
    ///     The task that completes when startup tasks finish.
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
    ///     Marks startup tasks as complete so continuous background services can start.
    /// </summary>
    public void SignalComplete()
    {
        _completionSource.TrySetResult();
    }
}
