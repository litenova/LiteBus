using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core persistence model for one stored audit checkpoint.
/// </summary>
/// <remarks>
///     <para>
///         Every cut checkpoint is stored as one of these rows before it is published, so verification can
///         compare a segment against its own checkpoints without an external store. The row identity is the
///         checkpoint identity, and the publish state, attempt count, and last error record the background
///         publication retries. Applications own migrations; apply
///         <see cref="AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> in
///         <c>OnModelCreating</c> or call the same fluent configuration from a design-time factory.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Entity Framework Core materializes entities
///         through their setters, the same documented exception the entity types of the relational adapters rely
///         on.
///     </para>
/// </remarks>
public sealed class AuditCheckpointEntity
{
    /// <summary>
    ///     Gets or sets the checkpoint identity.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the segment the checkpoint covers.
    /// </summary>
    public string Segment { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the sequence of the last document the checkpoint covers.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    ///     Gets or sets the number of documents in the segment at the checkpoint, which proves coverage.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    ///     Gets or sets the hash of the document at <see cref="Sequence" />.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the name of the hash scheme the covered document was written with.
    /// </summary>
    public string HashScheme { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets why the checkpoint was cut.
    /// </summary>
    public AuditCheckpointKind Kind { get; set; }

    /// <summary>
    ///     Gets or sets when the checkpoint was cut.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets the free-form label distinguishing checkpoints cut on different rhythms, or
    ///     <see langword="null" /> when the checkpoint carries no label.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    ///     Gets or sets the signing scheme of the signature over the checkpoint, or <see langword="null" />
    ///     when no signer is configured.
    /// </summary>
    public string? SignatureScheme { get; set; }

    /// <summary>
    ///     Gets or sets the encoded signature over the checkpoint, or <see langword="null" /> when no signer
    ///     is configured.
    /// </summary>
    public string? SignatureValue { get; set; }

    /// <summary>
    ///     Gets or sets every signature over the checkpoint, serialized as a JSON array in signing order.
    /// </summary>
    public string SignaturesJson { get; set; } = "[]";

    /// <summary>
    ///     Gets or sets whether the checkpoint reached its publishers.
    /// </summary>
    public CheckpointPublishState PublishState { get; set; }

    /// <summary>
    ///     Gets or sets how many publish attempts were made for the checkpoint.
    /// </summary>
    public int PublishAttempts { get; set; }

    /// <summary>
    ///     Gets or sets the last publish error, or <see langword="null" /> when no attempt failed yet.
    /// </summary>
    public string? LastPublishError { get; set; }
}
