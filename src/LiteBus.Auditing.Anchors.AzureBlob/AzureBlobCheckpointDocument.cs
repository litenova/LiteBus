using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     The checkpoint fields stored in one anchor blob.
/// </summary>
/// <param name="FormatVersion">The stored format version.</param>
/// <param name="Id">The checkpoint identifier.</param>
/// <param name="Segment">The segment the checkpoint covers.</param>
/// <param name="Sequence">The sequence of the last document the checkpoint covers.</param>
/// <param name="Count">The number of documents in the segment at the checkpoint, which proves coverage.</param>
/// <param name="Hash">The hash of the document at <paramref name="Sequence" />.</param>
/// <param name="HashScheme">The hash scheme the covered document was written with.</param>
/// <param name="Kind">Why the checkpoint was cut.</param>
/// <param name="CreatedAt">When the checkpoint was cut.</param>
/// <param name="Label">The free-form label distinguishing checkpoints cut on different rhythms.</param>
/// <param name="Signature">The signature over the checkpoint, or <see langword="null" /> when none was attached.</param>
/// <param name="Signatures">Every signature over the checkpoint.</param>
/// <remarks>
///     The stored copy carries no publish state: attempts and errors describe the stored checkpoint's progress toward
///     its publishers, while the anchor holds what was published.
/// </remarks>
internal sealed record AzureBlobCheckpointDocument(
    int FormatVersion,
    Guid Id,
    string Segment,
    long Sequence,
    long Count,
    string Hash,
    string HashScheme,
    AuditCheckpointKind Kind,
    DateTimeOffset CreatedAt,
    string? Label,
    AuditCheckpointSignature? Signature,
    List<AuditCheckpointSignature> Signatures)
{
    /// <summary>
    ///     The stored format version written by this package.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    ///     Stores a checkpoint as an anchor document.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to store.</param>
    /// <returns>The document written to the anchor blob.</returns>
    public static AzureBlobCheckpointDocument FromCheckpoint(AuditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return new AzureBlobCheckpointDocument(
            CurrentFormatVersion,
            checkpoint.Id,
            checkpoint.Segment,
            checkpoint.Sequence,
            checkpoint.Count,
            checkpoint.Hash,
            checkpoint.HashScheme,
            checkpoint.Kind,
            checkpoint.CreatedAt,
            checkpoint.Label,
            checkpoint.Signature,
            [.. checkpoint.Signatures]);
    }

    /// <summary>
    ///     Reads an anchor document back as a checkpoint.
    /// </summary>
    /// <param name="blobName">The anchor blob the document was read from, named when the document is incomplete.</param>
    /// <returns>The checkpoint the blob holds.</returns>
    /// <exception cref="AzureBlobCheckpointAnchorException">
    ///     Thrown when the document misses a checkpoint field the anchor cannot reconstruct.
    /// </exception>
    public AuditCheckpoint ToCheckpoint(string blobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        if (string.IsNullOrWhiteSpace(Segment) ||
            string.IsNullOrWhiteSpace(Hash) ||
            string.IsNullOrWhiteSpace(HashScheme) ||
            Signatures is null)
        {
            throw new AzureBlobCheckpointAnchorException(
                $"The checkpoint blob '{blobName}' does not hold a complete checkpoint.");
        }

        return new AuditCheckpoint(Id, Segment, Sequence, Count, Hash, HashScheme, Kind, CreatedAt)
        {
            Label = Label,
            Signature = Signature,
            Signatures = [.. Signatures],
        };
    }
}
