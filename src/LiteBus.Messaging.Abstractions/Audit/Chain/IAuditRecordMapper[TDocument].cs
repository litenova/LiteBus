namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Maps a LiteBus <see cref="AuditRecord" /> to the document type an audit trail stores.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     The mapper is where an application adds its own fields, such as a retention class, a pseudonymous actor or a
///     business time, to the stored document.
/// </remarks>
public interface IAuditRecordMapper<out TDocument>
{
    /// <summary>
    ///     Maps one audit record to a new stored document.
    /// </summary>
    /// <param name="record">The audit record LiteBus produced.</param>
    /// <returns>A new document; the chain writer fills its chain fields afterwards.</returns>
    TDocument Map(AuditRecord record);
}
