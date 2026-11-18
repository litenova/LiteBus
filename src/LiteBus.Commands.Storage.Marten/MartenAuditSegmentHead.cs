using Marten.Schema;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     The Marten document that carries the head of one audit chain segment for <see cref="MartenAuditTrail" />.
/// </summary>
/// <remarks>
///     <para>
///         One head per segment: the document identity is the segment name, and the head carries the sequence number
///         and hash of the last record appended. Every chained append loads the head, assigns the next sequence
///         number, and stores the head back in the same session as the record.
///     </para>
///     <para>
///         Optimistic concurrency serializes appends. Two writers that load the same head contend on one row, and the
///         loser fails its commit and retries against the winner, so two records never share a sequence number. Marten
///         creates the storage for this document on first use under its default auto-create setting.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Marten materializes documents through their
///         setters, the same documented exception the entity types of the relational adapters rely on.
///     </para>
/// </remarks>
[UseOptimisticConcurrency]
public sealed class MartenAuditSegmentHead
{
    /// <summary>
    ///     Gets or sets the segment name, which is the document identity.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the sequence number of the last record appended to the segment.
    /// </summary>
    public long LastSequence { get; set; }

    /// <summary>
    ///     Gets or sets the chain hash of the last record appended to the segment.
    /// </summary>
    public string? LastHash { get; set; }
}
