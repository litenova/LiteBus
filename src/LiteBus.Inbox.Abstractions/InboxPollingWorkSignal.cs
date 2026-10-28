using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Default inbox work signal that relies on polling delay only.
/// </summary>
public sealed class InboxPollingWorkSignal : IInboxWorkSignal, IDisposable
{
    /// <inheritdoc />
    /// <remarks>
    ///     A polling signal holds no resources; disposal does nothing.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return Task.Delay(pollInterval, cancellationToken);
    }
}
