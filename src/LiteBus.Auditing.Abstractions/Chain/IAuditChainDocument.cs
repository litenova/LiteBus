using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The chain fields every stored audit document carries when the audit trail is hash chained.
/// </summary>
/// <remarks>
///     An application that stores its own audit document type implements this interface on it, and the chain writer
///     fills the fields when it appends the document. The rest of the document is the application's own shape.
/// </remarks>
public interface IAuditChainDocument
{
    /// <summary>
    ///     Gets or sets the segment the document is chained in.
    /// </summary>
    string? Segment { get; set; }

    /// <summary>
    ///     Gets or sets the position of the document in its segment, starting at one.
    /// </summary>
    long Sequence { get; set; }

    /// <summary>
    ///     Gets or sets the hash of the previous document in the segment, or <see langword="null" /> for the first one.
    /// </summary>
    string? PreviousHash { get; set; }

    /// <summary>
    ///     Gets or sets the hash of this document.
    /// </summary>
    string? Hash { get; set; }

    /// <summary>
    ///     Gets or sets the name of the <see cref="AuditHashScheme{TDocument}" /> the hash was computed with.
    /// </summary>
    /// <value>
    ///     The scheme name, which verification uses to pick the canonical form, link rule and hasher per document.
    /// </value>
    string? HashScheme { get; set; }
}
