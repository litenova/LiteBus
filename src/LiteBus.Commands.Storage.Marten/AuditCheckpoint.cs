using System;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     A published anchor for a hash-chained audit segment: the sequence number and hash the chain had reached.
/// </summary>
/// <param name="Segment">The segment the checkpoint anchors, as the configured segment selector named it.</param>
/// <param name="Sequence">The sequence number of the last record covered by the checkpoint.</param>
/// <param name="Hash">The chain hash of the record at <paramref name="Sequence" />.</param>
/// <param name="CreatedAt">The time the checkpoint was published, read from the trail clock.</param>
/// <remarks>
///     <para>
///         A checkpoint lets a review prove that history was not rewritten: recompute the chain up to
///         <paramref name="Sequence" /> and compare its hash with <paramref name="Hash" />. A tamperer who rewrites a
///         whole suffix of the chain and recomputes every hash still cannot reproduce a checkpoint published earlier
///         to an independent store.
///     </para>
/// </remarks>
public sealed record AuditCheckpoint(string Segment, long Sequence, string Hash, DateTimeOffset CreatedAt);
