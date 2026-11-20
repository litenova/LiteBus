using System;
using LiteBus.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the Entity Framework Core audit store through <see cref="AuditingModuleBuilder" />.
/// </summary>
public static class AuditingModuleBuilderEntityFrameworkCoreExtensions
{
    /// <summary>
    ///     Stores audit records as Entity Framework Core entities, with the hash chain when the auditing module enables it.
    /// </summary>
    /// <typeparam name="TContext">The application database context type that carries the audit tables.</typeparam>
    /// <param name="builder">The auditing module builder.</param>
    /// <returns>The auditing module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The store records its open-generic trail and chain store, which the auditing module closes over
    ///         the configured document type, with <see cref="EfCoreAuditRecord" /> mapped by
    ///         <see cref="EfCoreAuditRecordMapper" /> and segmented by <see cref="EfCoreAuditRecordSegmenter" />
    ///         unless the chain configuration names its own. An explicit <c>StoreAs</c> or <c>SegmentBy</c> call
    ///         always wins over these defaults.
    ///     </para>
    ///     <para>
    ///         The context type parameter proves at compile time that the application database context joins
    ///         audit storage: it must derive from <see cref="DbContext" /> and implement
    ///         <see cref="IAuditDbContext" />. The scoped context joins the business unit of work, so captured
    ///         records commit or roll back with the handler change. The application registers its context
    ///         itself and maps the audit tables by calling
    ///         <see cref="AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> from
    ///         <c>OnModelCreating</c>. The store registers <see cref="IAuditDbContext" /> as the scoped context and
    ///         <see cref="IAuditDbContextFactory" /> over <c>IDbContextFactory&lt;TContext&gt;</c> as default ports
    ///         (see <see cref="EfCoreAuditStoreModule{TContext}" />), so the application registers its context with
    ///         <c>AddDbContextFactory&lt;TContext&gt;</c>. The chain store is a singleton that opens a short context
    ///         from the factory for every operation.
    ///     </para>
    /// </remarks>
    /// <exception cref="LiteBusConfigurationException">Another audit store is already registered.</exception>
    public static AuditingModuleBuilder UseEntityFrameworkCoreStore<TContext>(this AuditingModuleBuilder builder)
        where TContext : DbContext, IAuditDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Options;

        if (options.TrailType is not null ||
            options.ChainStoreType is not null ||
            options.StoreCapabilities is not null)
        {
            if (options.TrailType == typeof(EfCoreAuditTrail<>) &&
                options.ChainStoreType == typeof(EfCoreAuditChainStore<>))
            {
                return builder;
            }

            throw new LiteBusConfigurationException(
                $"An audit store is already registered (trail '{options.TrailType?.FullName}', " +
                $"chain store '{options.ChainStoreType?.FullName}'). Registering the Entity Framework Core audit store " +
                "conflicts with it. Register one audit store per composition.");
        }

        options.TrailType = typeof(EfCoreAuditTrail<>);
        options.ChainStoreType = typeof(EfCoreAuditChainStore<>);
        options.StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: true);
        options.StoreDefaultDocumentType = typeof(EfCoreAuditRecord);
        options.StoreDefaultMapperType = typeof(EfCoreAuditRecordMapper);
        options.StoreDefaultSegmenterType = typeof(EfCoreAuditRecordSegmenter);
        options.StoreDefaultCanonicalizerType = typeof(EfCoreAuditRecordCanonicalizer);

        builder.RegisterChildModule(new EfCoreAuditStoreModule<TContext>());

        return builder;
    }
}
