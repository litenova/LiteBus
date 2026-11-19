namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     The input an <see cref="IAuditLinkRule" /> combines into the bytes that are hashed.
/// </summary>
/// <param name="Segment">The segment the document is appended to.</param>
/// <param name="Sequence">The position of the document in the segment.</param>
/// <param name="PreviousHash">The hash of the previous document, or <see langword="null" /> for the first one.</param>
/// <param name="CanonicalBytes">The canonical bytes of the document.</param>
public sealed record AuditLinkInput(string Segment, long Sequence, string? PreviousHash, byte[] CanonicalBytes);
