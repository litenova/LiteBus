using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Default outbox work signal that relies on polling delay only.
/// </summary>
public sealed class OutboxPollingWorkSignal : IOutboxWorkSignal, IDisposable
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
