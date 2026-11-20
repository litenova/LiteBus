using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Maps a LiteBus <see cref="AuditRecord" /> to the document type an audit trail stores.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     The mapper is where an application adds its own fields, such as a retention class, a pseudonymous actor or a
///     business time, to the stored document.
/// </remarks>
public interface IAuditRecordMapper<TDocument>
{
    /// <summary>
    ///     Maps one audit record to a new stored document.
    /// </summary>
    /// <param name="record">The audit record LiteBus produced.</param>
    /// <returns>A new document; the chain writer fills its chain fields afterwards.</returns>
    TDocument Map(AuditRecord record);

    /// <summary>
    ///     Maps one audit record to a new stored document, with access to the mediation's scope.
    /// </summary>
    /// <param name="record">The audit record LiteBus produced.</param>
    /// <param name="context">The evaluation the mapping runs in: the mediation's scope and identity.</param>
    /// <param name="cancellationToken">The token used to cancel the mapping.</param>
    /// <returns>A new document; the chain writer fills its chain fields afterwards.</returns>
    /// <remarks>
    ///     <para>
    ///         Every audit store awaits this method, on the success path and on the out-of-band path that records
    ///         denials, failures and cancellations. The mapper is registered scoped and resolved from the mediation's
    ///         scope, so its constructor may take scoped services such as a pseudonym store or a tenant metadata
    ///         provider, and <see cref="AuditMappingContext.Services" /> is that same scope.
    ///     </para>
    ///     <para>
    ///         <see cref="AuditMappingContext.Features" /> holds the session the record is written on, so a row the
    ///         mapper writes through it commits or rolls back with the record: the business session on the success
    ///         path, and the record's own short session on the out-of-band path. The default implementation calls
    ///         <see cref="Map" />, so a mapper that needs no services keeps working unchanged.
    ///     </para>
    /// </remarks>
    ValueTask<TDocument> MapAsync(
        AuditRecord record,
        AuditMappingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(Map(record));
    }
}
