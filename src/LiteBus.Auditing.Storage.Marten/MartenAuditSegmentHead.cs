using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using Marten.Schema;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     The Marten document that carries the head of one audit chain segment for
///     <see cref="MartenAuditChainStore{TDocument}" />.
/// </summary>
/// <remarks>
///     <para>
///         One head per segment: the document identity is the segment key, and the head carries the sequence number
///         and hash of the last record appended. Every append loads the head, assigns the next sequence numbers, and
///         stores the head back in the same session as the records.
///     </para>
///     <para>
///         Optimistic concurrency serializes appends. Two writers that load the same head contend on one row, and the
///         loser fails its commit and retries against the winner, so two records never share a sequence number. Marten
///         creates the storage for this document on first use under its default auto-create setting.
///     </para>
///     <para>
///         The head also carries what listing, checkpointing, and retention need: the tenant and attributes the
///         segmenter described, when the segment opened, how many documents the segment holds, where the last
///         checkpoint was cut, and whether the segment has expired. Retention expires whole segments, and an expired
///         segment accepts no further records.
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
    ///     Gets or sets the segment key, which is the document identity.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the tenant the segment belongs to, or <see langword="null" /> when the segment is not
    ///     tenant-scoped.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Gets or sets the attributes such as retention class and year, stored so listing, expiry, and erasure can
    ///     select segments without parsing keys.
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets or sets when the segment was opened.
    /// </summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>
    ///     Gets or sets the sequence number of the last record appended to the segment.
    /// </summary>
    public long LastSequence { get; set; }

    /// <summary>
    ///     Gets or sets the chain hash of the last record appended to the segment.
    /// </summary>
    public string? LastHash { get; set; }

    /// <summary>
    ///     Gets or sets the number of linked documents stored in the segment, including a genesis document when the
    ///     segmenter wrote one.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    ///     Gets or sets the sequence number covered by the last checkpoint, or <see langword="null" /> when no
    ///     checkpoint was cut yet.
    /// </summary>
    public long? LastCheckpointSequence { get; set; }

    /// <summary>
    ///     Gets or sets when the last checkpoint was cut, or <see langword="null" /> when no checkpoint was cut yet.
    /// </summary>
    public DateTimeOffset? LastCheckpointAt { get; set; }

    /// <summary>
    ///     Gets or sets the name of the hash scheme the last record was written with, or <see langword="null" />
    ///     for heads written before schemes existed.
    /// </summary>
    /// <remarks>
    ///     Retained from the stored shape so heads written before the chain engine moved keep their scheme. New
    ///     heads leave it empty; the engine reads the scheme of the covered document instead.
    /// </remarks>
    public string? HashScheme { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the segment has expired and no longer accepts records.
    /// </summary>
    public bool Expired { get; set; }

    /// <summary>
    ///     Gets or sets the head version the store checks on a conditional write.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    ///     Creates a stored head document from a segment head.
    /// </summary>
    /// <param name="head">The segment head.</param>
    /// <returns>The stored document.</returns>
    public static MartenAuditSegmentHead FromAuditSegmentHead(AuditSegmentHead head)
    {
        ArgumentNullException.ThrowIfNull(head);

        return new MartenAuditSegmentHead
        {
            Id = head.Info.Key,
            TenantId = head.Info.TenantId,
            Attributes = new Dictionary<string, string>(head.Info.Attributes, StringComparer.Ordinal),
            OpenedAt = head.Info.OpenedAt,
            LastSequence = head.LastSequence,
            LastHash = head.LastHash,
            Count = head.Count,
            LastCheckpointSequence = head.LastCheckpointSequence,
            LastCheckpointAt = head.LastCheckpointAt,
            Expired = head.Expired,
            Version = head.Version
        };
    }

    /// <summary>
    ///     Reads the stored document back as a segment head.
    /// </summary>
    /// <returns>The segment head.</returns>
    public AuditSegmentHead ToAuditSegmentHead()
    {
        return new AuditSegmentHead(
            new AuditSegmentInfo(
                Id,
                TenantId,
                new Dictionary<string, string>(Attributes, StringComparer.Ordinal),
                OpenedAt),
            LastSequence,
            LastHash,
            Count,
            LastCheckpointSequence,
            LastCheckpointAt,
            Expired,
            Version);
    }

    /// <summary>
    ///     Copies the fields of an updated head onto this loaded document, in place.
    /// </summary>
    /// <param name="updated">The head after the append.</param>
    /// <remarks>
    ///     The loaded document is updated in place, never replaced, so the Marten optimistic concurrency check still
    ///     asserts the version captured at load. The identity and the segment metadata stay as stored; every other
    ///     field comes from <paramref name="updated" />.
    /// </remarks>
    internal void Apply(AuditSegmentHead updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        LastSequence = updated.LastSequence;
        LastHash = updated.LastHash;
        Count = updated.Count;
        LastCheckpointSequence = updated.LastCheckpointSequence;
        LastCheckpointAt = updated.LastCheckpointAt;
        Expired = updated.Expired;
        Version = updated.Version;
    }
}
