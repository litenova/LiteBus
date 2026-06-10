using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default outbox work signal that relies on polling delay only.
/// </summary>
public sealed class OutboxPollingWorkSignal : IOutboxWorkSignal
{
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
