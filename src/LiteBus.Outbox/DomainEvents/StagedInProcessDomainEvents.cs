using System;
using System.Collections.Generic;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Collects the domain events one mediation recorded for publication in process, so the commit listener can publish exactly those after the unit of work commits.
/// </summary>
/// <remarks>
///     Registered as a scoped service when collected domain event staging is enabled. A command batch shares one scope
///     across its commands, so one instance holds every command's recorded events. Access is synchronized because
///     handlers may record from parallel work inside one mediation.
/// </remarks>
internal sealed class StagedInProcessDomainEvents
{
    /// <summary>
    ///     Guards <see cref="_staged" />.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The recorded events, in recording order.
    /// </summary>
    private readonly List<StagedInProcessDomainEvent> _staged = [];

    /// <summary>
    ///     Gets a value indicating whether nothing is recorded.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _staged.Count == 0;
            }
        }
    }

    /// <summary>
    ///     Records one domain event for publication in process after the commit.
    /// </summary>
    /// <param name="staged">The recorded domain event with the cause its mediation carries.</param>
    public void Add(StagedInProcessDomainEvent staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        lock (_gate)
        {
            _staged.Add(staged);
        }
    }

    /// <summary>
    ///     Returns the recorded events in recording order and clears the collection, so one commit publishes each event once.
    /// </summary>
    /// <returns>The events recorded since the last call.</returns>
    public IReadOnlyList<StagedInProcessDomainEvent> Drain()
    {
        lock (_gate)
        {
            IReadOnlyList<StagedInProcessDomainEvent> drained = [.. _staged];
            _staged.Clear();
            return drained;
        }
    }
}
