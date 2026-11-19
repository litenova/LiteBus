using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Outbox.Storage.Marten.DomainEvents;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
using Marten;

namespace LiteBus.Outbox.Storage.Marten;

/// <summary>
///     Registers the Marten queued transactional outbox through <see cref="PostgreSqlOutboxModuleBuilder" />.
/// </summary>
public static class PostgreSqlOutboxModuleBuilderMartenExtensions
{
    /// <summary>
    ///     Registers scoped <see cref="ITransactionalOutbox" /> backed by the current Marten document session.
    /// </summary>
    /// <param name="builder">The PostgreSQL outbox module builder.</param>
    /// <returns>The PostgreSQL outbox module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The application registers Marten itself with <c>AddMarten</c> against the same database as the outbox;
    ///         LiteBus does not register Marten. Staged envelopes are queued on the scoped
    ///         <see cref="IDocumentSession" /> and commit with its <c>SaveChangesAsync</c> alongside documents.
    ///     </para>
    ///     <para>
    ///         This cannot be combined with <c>EnableAmbientTransactionProvider()</c>: both register
    ///         <see cref="ITransactionalOutbox" />, so module build throws when both are configured.
    ///     </para>
    /// </remarks>
    public static PostgreSqlOutboxModuleBuilder UseMartenTransactionalOutbox(this PostgreSqlOutboxModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.TransactionalOutboxFactory = serviceProvider => new MartenTransactionalOutbox(
            (IDocumentSession) (serviceProvider.GetService(typeof(IDocumentSession)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentSession))),
            (IOutboxEnvelopeFactory) (serviceProvider.GetService(typeof(IOutboxEnvelopeFactory)) ?? throw new LiteBusDependencyResolutionException(typeof(IOutboxEnvelopeFactory))),
            (PostgreSqlOutboxStoreRegistration) (serviceProvider.GetService(typeof(PostgreSqlOutboxStoreRegistration)) ?? throw new LiteBusDependencyResolutionException(typeof(PostgreSqlOutboxStoreRegistration))));

        return builder;
    }

    /// <summary>
    ///     Harvests domain events from the pending inserts and updates of the scoped Marten document session before collected events stage.
    /// </summary>
    /// <param name="builder">The PostgreSQL outbox module builder.</param>
    /// <returns>The PostgreSQL outbox module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Registers <see cref="MartenDomainEventHarvester" /> as a scoped
    ///         <see cref="LiteBus.Messaging.Abstractions.IDomainEventHarvester" />. The staging completion handler runs
    ///         the harvester before it drains the collector, so domain events buffered on tracked documents stage with
    ///         the same session save. Aggregates configured through
    ///         <see cref="OutboxModuleBuilder.HarvestDomainEvents{TAggregate}" /> harvest through their accessors, and
    ///         documents implementing <see cref="LiteBus.Messaging.Abstractions.IHasDomainEvents" /> harvest by default.
    ///     </para>
    ///     <para>
    ///         The application registers Marten itself with <c>AddMarten</c> against the same database as the outbox;
    ///         LiteBus does not register Marten.
    ///     </para>
    /// </remarks>
    public static PostgreSqlOutboxModuleBuilder HarvestDomainEvents(this PostgreSqlOutboxModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.DomainEventHarvesterFactory = serviceProvider => new MartenDomainEventHarvester(
            (IDocumentSession) (serviceProvider.GetService(typeof(IDocumentSession)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentSession))),
            (DomainEventAccessors) (serviceProvider.GetService(typeof(DomainEventAccessors)) ?? throw new LiteBusDependencyResolutionException(typeof(DomainEventAccessors))));

        return builder;
    }
}
