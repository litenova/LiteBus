using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     The Marten document that carries one stored audit checkpoint for
///     <see cref="MartenAuditChainStore{TDocument}" />.
/// </summary>
/// <remarks>
///     <para>
///         Every cut checkpoint is stored as one of these documents and then published, so
///         <see cref="MartenAuditCheckpointSource" /> can answer verification without an external store. The document
///         identity is the checkpoint identity; checkpoints of one segment order by sequence. The identity stays
///         stable across publish retries, so anchors never diverge.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Marten materializes documents through their
///         setters, the same documented exception the entity types of the relational adapters rely on.
///     </para>
/// </remarks>
public sealed class MartenAuditCheckpoint
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
    ///     Gets or sets the signature over the checkpoint, or <see langword="null" /> when no signer is
    ///     configured.
    /// </summary>
    public AuditCheckpointSignature? Signature { get; set; }

    /// <summary>
    ///     Gets or sets the free-form label distinguishing checkpoints cut on different rhythms, or
    ///     <see langword="null" /> when the checkpoint carries no label.
    /// </summary>
    public string? Label { get; set; }

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

    /// <summary>
    ///     Gets or sets every signature over the checkpoint, so a log signature, a timestamp, and witness
    ///     cosignatures can coexist.
    /// </summary>
    public List<AuditCheckpointSignature> Signatures { get; set; } = [];

    /// <summary>
    ///     Creates a stored document from a checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint that was cut.</param>
    /// <returns>The stored document.</returns>
    public static MartenAuditCheckpoint FromAuditCheckpoint(AuditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return new MartenAuditCheckpoint
        {
            Id = checkpoint.Id,
            Segment = checkpoint.Segment,
            Sequence = checkpoint.Sequence,
            Count = checkpoint.Count,
            Hash = checkpoint.Hash,
            HashScheme = checkpoint.HashScheme,
            Kind = checkpoint.Kind,
            CreatedAt = checkpoint.CreatedAt,
            Signature = checkpoint.Signature,
            Label = checkpoint.Label,
            PublishState = checkpoint.PublishState,
            PublishAttempts = checkpoint.PublishAttempts,
            LastPublishError = checkpoint.LastPublishError,
            Signatures = [.. checkpoint.Signatures]
        };
    }

    /// <summary>
    ///     Reads the stored document back as a checkpoint.
    /// </summary>
    /// <returns>The checkpoint.</returns>
    public AuditCheckpoint ToAuditCheckpoint()
    {
        return new AuditCheckpoint(Id, Segment, Sequence, Count, Hash, HashScheme, Kind, CreatedAt)
        {
            Signature = Signature,
            Label = Label,
            PublishState = PublishState,
            PublishAttempts = PublishAttempts,
            LastPublishError = LastPublishError,
            Signatures = [.. Signatures]
        };
    }
}
