using System;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core persistence model for the head of one audit chain segment.
/// </summary>
/// <remarks>
///     <para>
///         One row per segment, keyed by the segment name. The row carries the sequence number and hash of the
///         last linked document, the stored segment identity from <see cref="AuditSegmentInfo" />, and what
///         checkpointing needs: how many documents the segment holds, where the last checkpoint was cut, and
///         whether the segment expired. Applications own migrations; apply
///         <see cref="AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> in
///         <c>OnModelCreating</c> or call the same fluent configuration from a design-time factory.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Entity Framework Core materializes entities
///         through their setters, the same documented exception the entity types of the relational adapters rely
///         on.
///     </para>
/// </remarks>
public sealed class AuditSegmentHeadEntity
{
    /// <summary>
    ///     Gets or sets the segment name, which is the row identity.
    /// </summary>
    public string Segment { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the tenant the segment belongs to, or <see langword="null" /> when the segment is not
    ///     tenant-scoped.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Gets or sets the segment attributes, such as retention class and year, serialized as a JSON object
    ///     with keys in ordinal order.
    /// </summary>
    public string AttributesJson { get; set; } = "{}";

    /// <summary>
    ///     Gets or sets when the segment was opened.
    /// </summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>
    ///     Gets or sets the sequence number of the last linked document, or zero when nothing is linked.
    /// </summary>
    public long LastSequence { get; set; }

    /// <summary>
    ///     Gets or sets the hash of the last linked document, or <see langword="null" /> when nothing is
    ///     linked.
    /// </summary>
    public string? LastHash { get; set; }

    /// <summary>
    ///     Gets or sets the number of linked documents in the segment, including a genesis document when the
    ///     segmenter wrote one.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    ///     Gets or sets the sequence number covered by the last checkpoint, or <see langword="null" /> when no
    ///     checkpoint was cut yet.
    /// </summary>
    public long? LastCheckpointSequence { get; set; }

    /// <summary>
    ///     Gets or sets when the last checkpoint was cut, or <see langword="null" /> when no checkpoint was cut
    ///     yet.
    /// </summary>
    public DateTimeOffset? LastCheckpointAt { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the segment expired and no longer accepts records.
    /// </summary>
    public bool Expired { get; set; }

    /// <summary>
    ///     Gets or sets the head version the store checks on a conditional write.
    /// </summary>
    /// <remarks>
    ///     Configured as an Entity Framework concurrency token, so a tracked update that lost an append race
    ///     fails instead of overwriting the head committed by the winning writer.
    /// </remarks>
    public long Version { get; set; }
}
