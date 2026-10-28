using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Records events published through <see cref="IEventMediator" /> for test assertions.
/// </summary>
/// <remarks>
///     All members are thread-safe. <see cref="Events" /> returns a snapshot, so a held reference never observes later
///     publications and mutating it never affects the recorded history.
/// </remarks>
public sealed class TestEventMediator : IEventMediator
{
    /// <summary>
    ///     Gets events recorded by <see cref="PublishAsync" /> overloads.
    /// </summary>
    private readonly List<object> _events = [];

    /// <summary>
    ///     Gets the lock that serializes recording mutations and snapshot reads.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Gets a snapshot of the events recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    /// <inheritdoc />
    public Task PublishAsync(
        IEvent @event,
        EventMediationSettings? eventMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        lock (_sync)
        {
            _events.Add(@event);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(
        TEvent @event,
        EventMediationSettings? eventMediationSettings = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);

        lock (_sync)
        {
            _events.Add(@event);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Clears recorded events.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _events.Clear();
        }
    }
}
