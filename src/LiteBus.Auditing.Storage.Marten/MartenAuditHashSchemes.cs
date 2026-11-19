using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Auditing.Hashing;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     The hash schemes the Marten audit chain writes and verifies.
/// </summary>
/// <remarks>
///     A chain that moves to its own algorithm, key, or canonical form keeps accepting the scheme its history was
///     written with, so verification picks the scheme per record and history is never rewritten.
/// </remarks>
public static class MartenAuditHashSchemes
{
    /// <summary>
    ///     Gets the default scheme: the canonical JSON of every field with the SHA-256 digest.
    /// </summary>
    /// <value>
    ///     The scheme named <c>sha256-v1</c>, combining <see cref="MartenAuditRecordCanonicalizer" />,
    ///     <see cref="PreviousHashLinkRule" />, and <see cref="Sha256AuditHasher" />.
    /// </value>
    /// <remarks>
    ///     The canonical form already includes the segment, the sequence, and the previous hash, so the link rule
    ///     passes the canonical bytes through unchanged and binding the predecessor costs nothing twice.
    /// </remarks>
    public static AuditHashScheme<MartenAuditRecord> Sha256V1 { get; } = new AuditHashScheme<MartenAuditRecord>(
        "sha256-v1",
        new MartenAuditRecordCanonicalizer(),
        new PreviousHashLinkRule(),
        new Sha256AuditHasher());
}
