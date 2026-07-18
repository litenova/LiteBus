using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Gates a processor background loop for pause, resume, and drain operations.
/// </summary>
public interface IProcessorBackgroundControl
{
    /// <summary>
    ///     Gets a token that is cancelled when a drain request needs to interrupt the polling delay.
    /// </summary>
    /// <value>A token that is cancelled once when drain starts.</value>
    CancellationToken DrainRequestedToken { get; }

    /// <summary>
    ///     Gets a value indicating whether the loop should perform one final pass and exit.
    /// </summary>
    /// <value><see langword="true" /> when drain has been requested; otherwise <see langword="false" />.</value>
    bool IsDraining { get; }

    /// <summary>
    ///     Waits until the processor loop may enter the next pass and marks that pass as active.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A task that completes when the loop may proceed.</returns>
    Task WaitIfPausedAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Signals that the active processor pass has completed.
    /// </summary>
    void SignalPassComplete();

    /// <summary>
    ///     Signals that the processor loop exited after completing or failing the drain pass.
    /// </summary>
    /// <param name="exception">The failure that ended the drain pass, or <see langword="null" /> when it succeeded.</param>
    void SignalDrainComplete(Exception? exception);
}
