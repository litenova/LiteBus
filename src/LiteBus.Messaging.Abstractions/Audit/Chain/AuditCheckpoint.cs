using System;

namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     The position and hash an audit chain segment reached, published to an independent store as an anchor.
/// </summary>
/// <param name="Id">The checkpoint identifier.</param>
/// <param name="Segment">The segment the checkpoint covers.</param>
/// <param name="Sequence">The sequence of the last document the checkpoint covers.</param>
/// <param name="Count">The number of documents in the segment at the checkpoint, which proves coverage.</param>
/// <param name="Hash">The hash of the document at <paramref name="Sequence" />.</param>
/// <param name="HashScheme">The hash scheme the covered document was written with.</param>
/// <param name="Kind">Why the checkpoint was cut.</param>
/// <param name="CreatedAt">When the checkpoint was cut.</param>
public sealed record AuditCheckpoint(
    Guid Id,
    string Segment,
    long Sequence,
    long Count,
    string Hash,
    string HashScheme,
    AuditCheckpointKind Kind,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    ///     Gets the signature over the checkpoint, or <see langword="null" /> when no signer is configured.
    /// </summary>
    public AuditCheckpointSignature? Signature { get; init; }
}
