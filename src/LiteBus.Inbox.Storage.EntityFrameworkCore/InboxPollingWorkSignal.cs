using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Default inbox work signal registered by EF Core storage when no broker-specific signal is configured.
/// </summary>
public sealed class InboxPollingWorkSignal : IInboxWorkSignal
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
