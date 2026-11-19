using System;
using LiteBus.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Registers the PostgreSQL audit store through <see cref="AuditingModuleBuilder" />.
/// </summary>
public static class AuditingModuleBuilderPostgreSqlExtensions
{
    /// <summary>
    ///     Stores audit records as PostgreSQL rows, with the hash chain when the auditing module enables it.
    /// </summary>
    /// <param name="builder">The auditing module builder.</param>
    /// <param name="configure">The PostgreSQL audit store configuration action.</param>
    /// <returns>The auditing module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The store records its open-generic trail and chain store, which the auditing module closes over
    ///         the configured document type, with <see cref="PostgreSqlAuditRecord" /> mapped by
    ///         <see cref="PostgreSqlAuditRecordMapper" /> and segmented by
    ///         <see cref="PostgreSqlAuditRecordSegmenter" /> unless the chain configuration names its own. An
    ///         explicit <c>StoreAs</c> or <c>SegmentBy</c> call always wins over these defaults.
    ///     </para>
    ///     <para>
    ///         The data source and the table names live on <see cref="PostgreSqlAuditModule" />, registered as a
    ///         child of the auditing module carrying the settings from <paramref name="configure" />. Staging
    ///         joins the ambient business transaction through
    ///         <see cref="LiteBus.Storage.PostgreSql.IPostgreSqlTransactionProvider" /> when the application
    ///         registers one, so captured records commit or roll back with the handler change.
    ///     </para>
    ///     <para>
    ///         Calling this twice records the store once; only the first configuration action runs.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="LiteBusConfigurationException">Another audit store is already registered.</exception>
    public static AuditingModuleBuilder UsePostgreSqlStore(
        this AuditingModuleBuilder builder,
        Action<PostgreSqlAuditModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = builder.Options;

        if (options.TrailType is not null ||
            options.ChainStoreType is not null ||
            options.StoreCapabilities is not null)
        {
            if (options.TrailType == typeof(PostgreSqlAuditTrail<>) &&
                options.ChainStoreType == typeof(PostgreSqlAuditChainStore<>))
            {
                return builder;
            }

            throw new LiteBusConfigurationException(
                $"An audit store is already registered (trail '{options.TrailType?.FullName}', " +
                $"chain store '{options.ChainStoreType?.FullName}'). Registering the PostgreSQL audit store " +
                "conflicts with it. Register one audit store per composition.");
        }

        options.TrailType = typeof(PostgreSqlAuditTrail<>);
        options.ChainStoreType = typeof(PostgreSqlAuditChainStore<>);
        options.StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: true);
        options.StoreDefaultDocumentType = typeof(PostgreSqlAuditRecord);
        options.StoreDefaultMapperType = typeof(PostgreSqlAuditRecordMapper);
        options.StoreDefaultSegmenterType = typeof(PostgreSqlAuditRecordSegmenter);

        builder.RegisterChildModule(new PostgreSqlAuditModule(configure));

        return builder;
    }
}
