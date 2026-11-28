using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.DomainEvents;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.DomainEvents;

/// <summary>
///     Moves domain events from the entities a database context tracks into the <see cref="IDomainEventCollector" />.
/// </summary>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
/// <remarks>
///     <para>
///         Reads every <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry" /> of the scoped context
///         through <see cref="DomainEventAccessors" />, so configured aggregates and entities implementing
///         <see cref="IHasDomainEvents" /> stage with the same <c>SaveChanges</c> call. Entries are read in every
///         state: a loaded aggregate that raises an event without touching a mapped property stays
///         <see cref="EntityState.Unchanged" />, and its events still harvest.
///     </para>
///     <para>
///         Registered by the storage builder <c>HarvestDomainEvents</c> method. The staging completion handler runs the
///         harvester before it drains the collector.
///     </para>
/// </remarks>
public sealed class EfCoreDomainEventHarvester<TContext> : IDomainEventHarvester
    where TContext : DbContext
{
    /// <summary>
    ///     The shared accessor pairs that read one tracked entity.
    /// </summary>
    private readonly DomainEventAccessors _accessors;

    /// <summary>
    ///     The database context whose change tracker entries are harvested.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreDomainEventHarvester{TContext}" /> class.
    /// </summary>
    /// <param name="dbContext">The database context whose change tracker entries are harvested.</param>
    /// <param name="accessors">The shared accessor pairs that read one tracked entity.</param>
    public EfCoreDomainEventHarvester(TContext dbContext, DomainEventAccessors accessors)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(accessors);

        _dbContext = dbContext;
        _accessors = accessors;
    }

    /// <inheritdoc />
    public void HarvestInto(IDomainEventCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);

        foreach (var entry in _dbContext.ChangeTracker.Entries())
        {
            _accessors.Harvest(entry.Entity, collector);
        }
    }
}
