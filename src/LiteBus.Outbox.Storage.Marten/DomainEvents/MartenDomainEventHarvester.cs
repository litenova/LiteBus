using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.DomainEvents;
using Marten;

namespace LiteBus.Outbox.Storage.Marten.DomainEvents;

/// <summary>
///     Moves domain events from the documents a Marten session tracks into the <see cref="IDomainEventCollector" />.
/// </summary>
/// <remarks>
///     <para>
///         Reads the pending inserts and updates of the scoped <see cref="IDocumentSession" /> through
///         <see cref="DomainEventAccessors" />, so configured aggregates and documents implementing
///         <see cref="IHasDomainEvents" /> stage with the same session save. Pending deletions are ignored.
///     </para>
///     <para>
///         Registered by
///         <see cref="LiteBus.Outbox.Storage.Marten.PostgreSqlOutboxModuleBuilderMartenExtensions.HarvestDomainEvents" />.
///         The staging completion handler runs the harvester before it drains the collector.
///     </para>
/// </remarks>
public sealed class MartenDomainEventHarvester : IDomainEventHarvester
{
    /// <summary>
    ///     The shared accessor pairs that read one tracked document.
    /// </summary>
    private readonly DomainEventAccessors _accessors;

    /// <summary>
    ///     The document session whose pending inserts and updates are harvested.
    /// </summary>
    private readonly IDocumentSession _session;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenDomainEventHarvester" /> class.
    /// </summary>
    /// <param name="session">The document session whose pending inserts and updates are harvested.</param>
    /// <param name="accessors">The shared accessor pairs that read one tracked document.</param>
    public MartenDomainEventHarvester(IDocumentSession session, DomainEventAccessors accessors)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(accessors);

        _session = session;
        _accessors = accessors;
    }

    /// <inheritdoc />
    public void HarvestInto(IDomainEventCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);

        var pending = _session.PendingChanges;

        foreach (var document in pending.Inserts())
        {
            HarvestDocument(document, collector);
        }

        foreach (var document in pending.Updates())
        {
            HarvestDocument(document, collector);
        }
    }

    /// <summary>
    ///     Harvests one pending document unless the session returned no instance for it.
    /// </summary>
    /// <param name="document">The pending document instance, if any.</param>
    /// <param name="collector">The collector the outbox stages from.</param>
    private void HarvestDocument(object? document, IDomainEventCollector collector)
    {
        if (document is null)
        {
            return;
        }

        _accessors.Harvest(document, collector);
    }
}
