using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default scoped <see cref="IDomainEventCollector" /> that buffers collected domain events until the staging completion handler drains them.
/// </summary>
/// <remarks>
///     Registered by <see cref="Abstractions.OutboxModuleBuilder.StageCollectedEvents" />. One instance is shared by every
///     handler in a mediation through the dispatch scope, and event mediations may invoke handlers concurrently, so
///     collection and draining are serialized with a lock.
/// </remarks>
internal sealed class DomainEventCollector : IDomainEventCollector
{
    /// <summary>
    ///     The collected domain events in collection order.
    /// </summary>
    private readonly List<object> _events = [];

    /// <summary>
    ///     The lock that serializes collection and draining.
    /// </summary>
    private readonly object _sync = new();

    /// <inheritdoc />
    public void Collect(object domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        lock (_sync)
        {
            _events.Add(domainEvent);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<object> Drain()
    {
        lock (_sync)
        {
            object[] drained = [.. _events];
            _events.Clear();
            return drained;
        }
    }
}
