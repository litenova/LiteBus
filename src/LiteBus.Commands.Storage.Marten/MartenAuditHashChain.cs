using System;
using System.Security.Cryptography;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Computes the SHA-256 chain hash over the canonical JSON of a Marten audit record.
/// </summary>
/// <remarks>
///     <para>
///         The chain itself no longer hashes through this type: <see cref="MartenAuditTrail" /> writes and
///         <see cref="MartenAuditChain" /> verifies through <see cref="MartenAuditRecordCanonicalizer" /> with the
///         <see cref="MartenAuditHashSchemes.Sha256V1" /> scheme, which carries the same field set this method always
///         hashed. This method remains so code that pinned a hash before schemes keeps a stable entry point, and it
///         delegates to the canonicalizer so the two can never drift.
///     </para>
/// </remarks>
public static class MartenAuditHashChain
{
    /// <summary>
    ///     Computes the chain hash for one audit document.
    /// </summary>
    /// <param name="record">The document to hash, with its segment, sequence, and predecessor hash assigned.</param>
    /// <returns>The lowercase hexadecimal SHA-256 over the canonical JSON of the document.</returns>
    public static string ComputeHash(MartenAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var canonical = new MartenAuditRecordCanonicalizer().Canonicalize(record);
        var bytes = SHA256.HashData(canonical);

        return Convert.ToHexStringLower(bytes);
    }
}
