using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

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
    ///     Runs in the mediation's scope before commit, so the mapper may resolve scoped services such as a
    ///     pseudonym store. The default implementation calls <see cref="Map" />, so a mapper that needs no services
    ///     keeps working unchanged.
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
