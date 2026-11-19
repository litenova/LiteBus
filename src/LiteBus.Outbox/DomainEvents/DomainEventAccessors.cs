using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Harvests domain events from tracked aggregates into the mediation collector, using configured accessors with an
///     <see cref="IHasDomainEvents" /> fallback.
/// </summary>
/// <remarks>
///     <para>
///         Registered once as a singleton by the outbox module and read by every storage harvester, so configured
///         accessor pairs apply no matter which storage adapter tracks the aggregates.
///     </para>
///     <para>
///         A harvester clears the events it moved from each entity, so harvesting twice in one unit of work stages each
///         event once. Clearing also keeps nested mediations safe: a child mediation harvests and clears first, and the
///         parent finds nothing left to harvest.
///     </para>
/// </remarks>
public sealed class DomainEventAccessors
{
    /// <summary>
    ///     The configured accessor pairs in registration order.
    /// </summary>
    private readonly DomainEventAccessor[] _accessors;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainEventAccessors" /> class.
    /// </summary>
    /// <param name="accessors">The configured accessor pairs.</param>
    internal DomainEventAccessors(IReadOnlyList<DomainEventAccessor> accessors)
    {
        ArgumentNullException.ThrowIfNull(accessors);

        _accessors = [.. accessors];
    }

    /// <summary>
    ///     Moves the domain events of one tracked entity into the collector and clears them on the entity.
    /// </summary>
    /// <param name="entity">The tracked aggregate or document instance to harvest.</param>
    /// <param name="collector">The collector the outbox stages from.</param>
    /// <remarks>
    ///     A configured accessor for the entity type wins; otherwise an entity implementing
    ///     <see cref="IHasDomainEvents" /> is harvested by default. Any other entity is ignored. Events are collected
    ///     before the entity is cleared, so a failure leaves the buffered events on the entity.
    /// </remarks>
    public void Harvest(object entity, IDomainEventCollector collector)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(collector);

        var accessor = FindAccessor(entity.GetType());

        if (accessor is not null)
        {
            HarvestWithAccessor(entity, accessor, collector);
            return;
        }

        if (entity is IHasDomainEvents buffered)
        {
            foreach (var domainEvent in buffered.DomainEvents)
            {
                collector.Collect(domainEvent);
            }

            buffered.ClearDomainEvents();
        }
    }

    /// <summary>
    ///     Moves the events read by one configured accessor into the collector and clears them on the entity.
    /// </summary>
    /// <param name="entity">The tracked aggregate instance to harvest.</param>
    /// <param name="accessor">The configured accessor that reads the entity type.</param>
    /// <param name="collector">The collector the outbox stages from.</param>
    private static void HarvestWithAccessor(object entity, DomainEventAccessor accessor, IDomainEventCollector collector)
    {
        foreach (var domainEvent in accessor.ReadEvents(entity))
        {
            collector.Collect(domainEvent);
        }

        accessor.ClearEvents(entity);
    }

    /// <summary>
    ///     Finds the configured accessor for one entity type.
    /// </summary>
    /// <param name="entityType">The runtime type of the tracked entity.</param>
    /// <returns>The matching accessor, or <see langword="null" /> when none matches.</returns>
    /// <remarks>
    ///     An exact type match wins first; otherwise the first accessor whose aggregate type the entity type derives
    ///     from or implements is used, so proxies and subclasses harvest through the configured aggregate.
    /// </remarks>
    private DomainEventAccessor? FindAccessor(Type entityType)
    {
        DomainEventAccessor? assignable = null;

        foreach (var candidate in _accessors)
        {
            if (candidate.AggregateType == entityType)
            {
                return candidate;
            }

            if (assignable is null && candidate.AggregateType.IsAssignableFrom(entityType))
            {
                assignable = candidate;
            }
        }

        return assignable;
    }
}
