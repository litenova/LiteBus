using System;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     The Marten document that records when one audit document was staged but not linked yet, so the linker can
///     assign chain positions in commit order.
/// </summary>
/// <remarks>
///     <para>
///         One entry per staged document, written in the same session as the document itself: both commit together
///         and both roll back together. The entry carries the segment, the document identity as text, and when the
///         document was staged. <see cref="MartenAuditChainStore{TDocument}.ListUnlinkedAsync" /> orders staged
///         documents by this entry, and <see cref="MartenAuditChainStore{TDocument}.AppendAsync" /> removes the
///         entries of the documents it links.
///     </para>
///     <para>
///         The document identity is stored as text because the stored document type is generic: the store reads the
///         <c>Id</c> property the Marten identity convention requires and converts it with the invariant culture. A
///         staged document whose entry is missing, for example one written before entries existed, is still returned
///         by the unlinked listing, after the entries in storage order.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Marten materializes documents through their
///         setters, the same documented exception the entity types of the relational adapters rely on.
///     </para>
/// </remarks>
public sealed class MartenAuditUnlinkedEntry
{
    /// <summary>
    ///     Gets or sets the entry identity, assigned when the document is staged.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the segment the staged document belongs to.
    /// </summary>
    public string Segment { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the staged document identity as text, converted with the invariant culture.
    /// </summary>
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the tenant the staged document belongs to, or <see langword="null" /> when the stored
    ///     document type carries no tenant.
    /// </summary>
    /// <remarks>
    ///     Read best-effort from the <c>TenantId</c> property of the staged document when it has one, so erasure by
    ///     tenant also removes staged documents whose segment has no head yet.
    /// </remarks>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Gets or sets when the document was staged.
    /// </summary>
    public DateTimeOffset StagedAt { get; set; }
}
