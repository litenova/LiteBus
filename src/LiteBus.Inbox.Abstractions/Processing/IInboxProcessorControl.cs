using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Controls pause, resume, and drain behavior for the inbox processor background loop.
/// </summary>
public interface IInboxProcessorControl
{
    /// <summary>
    ///     Gets the current processor loop state.
    /// </summary>
    /// <value>The state observed by callers without acquiring the processor gate.</value>
    ProcessorState State { get; }

    /// <summary>
    ///     Suspends leasing after the current pass completes.
    /// </summary>
    /// <remarks>
    ///     Blocks until the running pass finishes before returning.
    /// </remarks>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A task that completes when the processor loop is paused.</returns>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Resumes leasing after a pause.
    /// </summary>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>A task that completes when the processor loop is running again.</returns>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Processes messages currently in the store once, then stops leasing.
    /// </summary>
    /// <remarks>
    ///     Useful for graceful shutdown: call before host stop, await completion, then cancel the host.
    /// </remarks>
    /// <param name="timeout">The maximum time to wait for the drain pass to complete.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for drain completion.</param>
    /// <returns>A task that completes when the processor loop exits after the final pass.</returns>
    Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}