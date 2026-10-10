using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Allows a processor loop to wait for new work or fall back to a polling delay.
/// </summary>
/// <remarks>
///     A work signal can hold a dedicated connection and a background loop, such as a PostgreSQL <c>LISTEN</c>
///     subscription, so it is disposable and the container that created it owns its disposal. An implementation
///     registered with a container that may be disposed synchronously should also implement <see cref="IDisposable" />,
///     because Microsoft dependency injection refuses to dispose an <see cref="IAsyncDisposable" />-only service
///     synchronously.
/// </remarks>
public interface IProcessorWorkSignal : IAsyncDisposable
{
    /// <summary>
    ///     Waits until work may be available or the polling interval elapses.
    /// </summary>
    /// <param name="pollInterval">The maximum time to wait before the processor should poll again.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>A task that completes when the wait ends.</returns>
    Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default);
}
