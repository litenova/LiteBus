using System;
using LiteBus.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     Registers the Marten audit store through <see cref="AuditingModuleBuilder" />.
/// </summary>
public static class AuditingModuleBuilderMartenExtensions
{
    /// <summary>
    ///     Stores audit records as Marten documents, with the hash chain when the auditing module enables it.
    /// </summary>
    /// <param name="builder">The auditing module builder.</param>
    /// <returns>The auditing module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The store records its open-generic trail and chain store, which the auditing module closes over
    ///         the configured document type, with <see cref="MartenAuditRecord" /> mapped by
    ///         <see cref="MartenAuditRecordMapper" /> and segmented by <see cref="MartenAuditRecordSegmenter" />
    ///         unless the chain configuration names its own. An explicit <c>StoreAs</c> or <c>SegmentBy</c> call
    ///         always wins over these defaults.
    ///     </para>
    ///     <para>
    ///         The Marten session joins the business unit of work, so captured records commit or roll back with
    ///         the handler change. The application registers Marten itself with <c>AddMarten</c>, which supplies
    ///         both the scoped session and the document store; LiteBus does not register Marten.
    ///     </para>
    /// </remarks>
    /// <exception cref="LiteBusConfigurationException">Another audit store is already registered.</exception>
    public static AuditingModuleBuilder UseMartenStore(this AuditingModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Options;

        if (options.TrailType is not null ||
            options.ChainStoreType is not null ||
            options.StoreCapabilities is not null)
        {
            if (options.TrailType == typeof(MartenAuditTrail<>) &&
                options.ChainStoreType == typeof(MartenAuditChainStore<>))
            {
                return builder;
            }

            throw new LiteBusConfigurationException(
                $"An audit store is already registered (trail '{options.TrailType?.FullName}', " +
                $"chain store '{options.ChainStoreType?.FullName}'). Registering the Marten audit store " +
                "conflicts with it. Register one audit store per composition.");
        }

        options.TrailType = typeof(MartenAuditTrail<>);
        options.ChainStoreType = typeof(MartenAuditChainStore<>);
        options.StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: true);
        options.StoreDefaultDocumentType = typeof(MartenAuditRecord);
        options.StoreDefaultMapperType = typeof(MartenAuditRecordMapper);
        options.StoreDefaultSegmenterType = typeof(MartenAuditRecordSegmenter);

        return builder;
    }
}
