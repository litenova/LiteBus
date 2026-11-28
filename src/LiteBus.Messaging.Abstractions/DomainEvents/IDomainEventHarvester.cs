namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Moves domain events from the entities a unit of work tracks into the <see cref="IDomainEventCollector" />.
/// </summary>
/// <remarks>
///     <para>
///         A storage adapter implements this against what it tracks: the pending changes of a document session, or the
///         change tracker of a database context. The outbox runs every registered harvester before it stages the
///         collected events, so handlers never copy events from aggregates to the collector by hand.
///     </para>
///     <para>
///         A harvester clears the events it moved from each entity, so harvesting twice in one unit of work stages each
///         event once.
///     </para>
/// </remarks>
public interface IDomainEventHarvester
{
    /// <summary>
    ///     Collects the domain events of every tracked entity this harvester reads, and clears them on the entities.
    /// </summary>
    /// <param name="collector">The collector the outbox stages from.</param>
    void HarvestInto(IDomainEventCollector collector);
}
