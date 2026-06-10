using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default inbox work signal that relies on polling delay only.
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
