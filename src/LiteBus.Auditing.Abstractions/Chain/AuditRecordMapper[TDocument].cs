using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Base class for audit record mappers that need no services and map synchronously.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     A derived class implements <see cref="Map" />, and <see cref="MapAsync" /> calls it with the context every store
///     provides. A mapper that needs scoped services or the store session implements
///     <see cref="IAuditRecordMapper{TDocument}" /> directly and supplies its own <see cref="MapAsync" />.
/// </remarks>
public abstract class AuditRecordMapper<TDocument> : IAuditRecordMapper<TDocument>
{
    /// <summary>
    ///     Maps one audit record to a new stored document.
    /// </summary>
    /// <param name="record">The audit record LiteBus produced.</param>
    /// <returns>A new document; the chain writer fills its chain fields afterwards.</returns>
    public abstract TDocument Map(AuditRecord record);

    /// <summary>
    ///     Maps one audit record to a new stored document by calling <see cref="Map" />.
    /// </summary>
    /// <param name="record">The audit record LiteBus produced.</param>
    /// <param name="context">The evaluation the mapping runs in.</param>
    /// <param name="cancellationToken">The token used to cancel the mapping.</param>
    /// <returns>A new document; the chain writer fills its chain fields afterwards.</returns>
    public ValueTask<TDocument> MapAsync(
        AuditRecord record,
        AuditMappingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(Map(record));
    }
}
