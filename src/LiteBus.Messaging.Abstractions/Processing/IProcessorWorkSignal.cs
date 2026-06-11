using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Allows a processor loop to wait for new work or fall back to a polling delay.
/// </summary>
public interface IProcessorWorkSignal
{
    /// <summary>
    ///     Waits until work may be available or the polling interval elapses.
    /// </summary>
    /// <param name="pollInterval">The maximum time to wait before the processor should poll again.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>A task that completes when the wait ends.</returns>
    Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default);
}