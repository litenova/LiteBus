using System;
using LiteBus.Auditing.Abstractions;
using System.Buffers.Binary;
using System.Text;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Binds the chain position of an audit document into the hashed bytes with a length-prefixed frame.
/// </summary>
/// <remarks>
///     <para>
///         The default <see cref="IAuditLinkRule" /> for new schemes: it prepends a frame carrying the scheme name, the
///         segment, the sequence, and the previous hash to <see cref="AuditLinkInput.CanonicalBytes" />, so the
///         canonicalizer only ever describes the record. A consumer that writes its own canonical form over business
///         fields only still gets a linked chain: records cannot be reordered, moved between segments, or replaced
///         without a break.
///     </para>
///     <para>
///         The framed bytes are the concatenation of the frame and the canonical bytes. The frame is, in order: the
///         scheme name as a four-byte big-endian byte count followed by its UTF-8 bytes; the segment the same way; the
///         sequence as eight bytes, big-endian; and the previous hash as a four-byte big-endian byte count followed by
///         its UTF-8 bytes, or the four bytes <c>FF FF FF FF</c> with no bytes after them when the previous hash is
///         <see langword="null" />. The null marker keeps a first record distinct from a record carrying an empty
///         previous hash, and the length prefixes keep every field boundary unambiguous no matter which characters the
///         values hold.
///     </para>
/// </remarks>
public sealed class FramedLinkRule : IAuditLinkRule
{
    /// <summary>
    ///     The length marker written for a missing previous hash. Held as a constant because it is part of the
    ///     persisted framing: a first record must never frame like a record carrying an empty previous hash.
    /// </summary>
    private const uint NullPreviousHashLength = 0xFFFFFFFF;

    /// <summary>
    ///     The scheme name this rule frames. Fixed for the life of the rule.
    /// </summary>
    private readonly string _schemeName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FramedLinkRule" /> class for the current shipped scheme.
    /// </summary>
    /// <remarks>
    ///     The rule frames <see cref="AuditHashSchemeNames.Sha256V2" />. A scheme under any other name is framed by
    ///     passing its name to the constructor taking a scheme name instead, which is what
    ///     <see cref="AuditHashSchemeRegistry" /> does when it builds a scheme.
    /// </remarks>
    public FramedLinkRule()
        : this(AuditHashSchemeNames.Sha256V2)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FramedLinkRule" /> class for the named scheme.
    /// </summary>
    /// <param name="schemeName">The stable scheme name this rule frames, stored on every document.</param>
    public FramedLinkRule(string schemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);

        _schemeName = schemeName;
    }

    /// <summary>
    ///     Gets the stable scheme name this rule frames.
    /// </summary>
    public string SchemeName => _schemeName;

    /// <inheritdoc />
    public byte[] Link(AuditLinkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Segment);
        ArgumentNullException.ThrowIfNull(input.CanonicalBytes);

        byte[] schemeBytes = Encoding.UTF8.GetBytes(_schemeName);
        byte[] segmentBytes = Encoding.UTF8.GetBytes(input.Segment);
        byte[]? previousBytes = input.PreviousHash is null ? null : Encoding.UTF8.GetBytes(input.PreviousHash);

        byte[] linked = new byte[
            4 + schemeBytes.Length + 4 + segmentBytes.Length + 8 + 4 + (previousBytes?.Length ?? 0) + input.CanonicalBytes.Length];
        var offset = 0;

        WriteLengthPrefixed(linked, ref offset, schemeBytes);
        WriteLengthPrefixed(linked, ref offset, segmentBytes);

        BinaryPrimitives.WriteUInt64BigEndian(linked.AsSpan(offset, 8), unchecked((ulong) input.Sequence));
        offset += 8;

        if (previousBytes is null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(linked.AsSpan(offset, 4), NullPreviousHashLength);
            offset += 4;
        }
        else
        {
            WriteLengthPrefixed(linked, ref offset, previousBytes);
        }

        input.CanonicalBytes.CopyTo(linked, offset);

        return linked;
    }

    /// <summary>
    ///     Writes one length-prefixed value into the framed bytes.
    /// </summary>
    /// <param name="destination">The framed bytes under construction.</param>
    /// <param name="offset">The write position, advanced past the written value.</param>
    /// <param name="value">The value bytes to write.</param>
    private static void WriteLengthPrefixed(byte[] destination, ref int offset, byte[] value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, 4), unchecked((uint) value.Length));
        offset += 4;

        value.CopyTo(destination, offset);
        offset += value.Length;
    }
}
