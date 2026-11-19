namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Produces the exact bytes of a stored audit document that the chain hashes.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     The canonical form decides which fields are tamper-evident, their order, their encoding and how nulls and
///     times are written. It must exclude <see cref="IAuditChainDocument.Hash" /> and must be deterministic: the same
///     document always yields the same bytes.
/// </remarks>
public interface IAuditCanonicalizer<in TDocument>
{
    /// <summary>
    ///     Returns the canonical bytes of a document.
    /// </summary>
    /// <param name="document">The document whose chain fields, other than the hash, are already set.</param>
    /// <returns>The canonical bytes.</returns>
    byte[] Canonicalize(TDocument document);
}
