using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.InMemory;

/// <summary>
///     Registers the in-memory audit store through <see cref="AuditingModuleBuilder" />.
/// </summary>
public static class AuditingModuleBuilderInMemoryExtensions
{
    /// <summary>
    ///     Stores audit records in memory for unit tests, with the hash chain when the auditing module enables it.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type the chain writes.</typeparam>
    /// <param name="builder">The auditing module builder.</param>
    /// <returns>The auditing module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The store records its open-generic chain store, which the auditing module closes over the
    ///         configured document type, with <typeparamref name="TDocument" /> as the default. An explicit
    ///         <c>StoreAs</c> call always wins over that default. The in-memory trail records written records
    ///         for assertions; chain staging in tests goes through the chain store directly.
    ///     </para>
    ///     <para>
    ///         No mapper or segmenter default is recorded: a chained composition names both explicitly through
    ///         <c>StoreAs</c> and <c>SegmentBy</c>.
    ///     </para>
    ///     <para>
    ///         The in-memory store never joins the business transaction, so composition selects
    ///         <see cref="AuditCapture.AfterCommitThroughOutbox" /> for a requested WithChange capture and
    ///         reports the choice in the composition model.
    ///     </para>
    /// </remarks>
    /// <exception cref="AuditConfigurationException">Another audit store is already registered.</exception>
    public static AuditingModuleBuilder UseInMemoryStore<TDocument>(this AuditingModuleBuilder builder)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Options;

        if (options.TrailType is not null ||
            options.ChainStoreType is not null ||
            options.StoreCapabilities is not null)
        {
            if (options.TrailType == typeof(InMemoryAuditTrail) &&
                options.ChainStoreType == typeof(InMemoryAuditChainStore<>) &&
                options.StoreDefaultDocumentType == typeof(TDocument))
            {
                return builder;
            }

            throw new AuditConfigurationException(
                $"An audit store is already registered (trail '{options.TrailType?.FullName}', " +
                $"chain store '{options.ChainStoreType?.FullName}'). Registering the in-memory audit store " +
                "conflicts with it. Register one audit store per composition.");
        }

        options.TrailType = typeof(InMemoryAuditTrail);
        options.ChainStoreType = typeof(InMemoryAuditChainStore<>);
        options.StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: false);
        options.StoreDefaultDocumentType = typeof(TDocument);

        return builder;
    }
}
