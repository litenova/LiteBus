namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Decides how a document's hash includes its predecessor.
/// </summary>
/// <remarks>
///     The rule turns the canonical bytes and the chain position into the bytes the hasher receives, for example by
///     appending the previous hash, or the previous hash and the sequence.
/// </remarks>
public interface IAuditLinkRule
{
    /// <summary>
    ///     Returns the bytes to hash for one document.
    /// </summary>
    /// <param name="input">The canonical bytes and chain position of the document.</param>
    /// <returns>The bytes the hasher receives.</returns>
    byte[] Link(AuditLinkInput input);
}
