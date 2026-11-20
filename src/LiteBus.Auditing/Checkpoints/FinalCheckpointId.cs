using System;
using System.Security.Cryptography;
using System.Text;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     Derives the identifier of a segment's final checkpoint from the segment name.
/// </summary>
/// <remarks>
///     A segment has at most one final checkpoint: it is cut when the segment expires, and an expired segment takes no
///     more records. Deriving the identifier from the segment lets a retried expiry find the final checkpoint an
///     earlier attempt stored, whether that attempt stopped before or after it marked the head expired, instead of
///     cutting a second one.
/// </remarks>
internal static class FinalCheckpointId
{
    /// <summary>
    ///     The prefix hashed with the segment name, so the identifier space is LiteBus's own.
    /// </summary>
    private const string Namespace = "litebus.audit.final-checkpoint\n";

    /// <summary>
    ///     Gets the identifier of a segment's final checkpoint.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The identifier, the same for the same segment on every call.</returns>
    public static Guid For(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(Namespace + segment), hash);

        var bytes = hash[..16];

        // Mark the identifier as RFC 9562 version 8 (custom) with the RFC variant, so it is a well-formed UUID.
        bytes[6] = (byte) ((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte) ((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
