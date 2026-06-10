using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Default outbox work signal registered by EF Core storage when no broker-specific signal is configured.
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
