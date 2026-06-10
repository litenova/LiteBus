using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Gates a processor background loop for pause, resume, and drain operations.
/// </summary>
public interface IProcessorBackgroundControl
{
    /// <summary>
    ///     Waits until the processor loop may enter the next pass.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A task that completes when the loop may proceed.</returns>
    Task WaitIfPausedAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Gets a value indicating whether the loop should perform one final pass and exit.
    /// </summary>
    /// <value><see langword="true" /> when drain has been requested; otherwise <see langword="false" />.</value>
    bool IsDraining { get; }

    /// <summary>
    ///     Signals that the processor loop exited after completing the drain pass.
    /// </summary>
    void SignalDrainComplete();
}
