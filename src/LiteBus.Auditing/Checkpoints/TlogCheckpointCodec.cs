using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     Renders audit checkpoints in the C2SP transparency-log checkpoint text shape and reads them back.
/// </summary>
/// <remarks>
///     <para>
///         The checkpoint text follows <c>c2sp.org/tlog-checkpoint</c>: the origin line carries the segment, the tree
///         size carries the covered document count, and the root hash carries the checkpoint hash decoded to bytes and
///         encoded as base64, so witnesses, timestamping authorities, and generic verifiers consume LiteBus anchors
///         without LiteBus code. LiteBus fields that the shape has no room for travel as extension lines, which a
///         generic verifier ignores and this codec reads back.
///     </para>
///     <para>
///         Each signature renders as one signed-note line carrying the scheme as the key name and the value as the
///         base64 signature. A checkpoint with no signatures renders its note text and the separating blank line only;
///         that output is the signing input, not a valid signed note, until a signer adds a line.
///     </para>
/// </remarks>
public static class TlogCheckpointCodec
{
    /// <summary>
    ///     The signature scheme recorded when a parsed checkpoint carries no scheme extension.
    /// </summary>
    private const string UnknownSchemeName = "unknown";

    /// <summary>
    ///     Renders a checkpoint as C2SP checkpoint text.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to render.</param>
    /// <returns>The checkpoint text with one signature line per signature.</returns>
    /// <exception cref="ArgumentException">
    ///     The checkpoint carries a value the text shape cannot hold, such as a label or hash with a line break, or a
    ///     signature with an empty scheme or a non-canonical base64 value.
    /// </exception>
    public static string Encode(AuditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.HashScheme);

        if (checkpoint.Segment.Contains('\r') || checkpoint.Segment.Contains('\n'))
        {
            throw new ArgumentException("The checkpoint segment must not contain a line break.", nameof(checkpoint));
        }

        if (checkpoint.Hash.Contains('\r') || checkpoint.Hash.Contains('\n'))
        {
            throw new ArgumentException("The checkpoint hash must not contain a line break.", nameof(checkpoint));
        }

        if (checkpoint.HashScheme.Contains('\r') || checkpoint.HashScheme.Contains('\n'))
        {
            throw new ArgumentException("The checkpoint hash scheme must not contain a line break.", nameof(checkpoint));
        }

        if (checkpoint.Label is not null &&
            (string.IsNullOrWhiteSpace(checkpoint.Label) ||
                checkpoint.Label.Contains('\r') ||
                checkpoint.Label.Contains('\n')))
        {
            throw new ArgumentException("The checkpoint label must not be empty or contain a line break.", nameof(checkpoint));
        }

        var signatures = SelectSignatures(checkpoint);

        foreach (var signature in signatures)
        {
            ValidateSignature(signature);
        }

        StringBuilder text = new();
        text.Append(checkpoint.Segment).Append('\n');
        text.Append(checkpoint.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append(ToRootHash(checkpoint.Hash)).Append('\n');
        text.Append("sequence: ").Append(checkpoint.Sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("scheme: ").Append(checkpoint.HashScheme).Append('\n');
        text.Append("kind: ").Append(checkpoint.Kind).Append('\n');

        if (checkpoint.Label is not null)
        {
            text.Append("label: ").Append(checkpoint.Label).Append('\n');
        }

        text.Append("created: ").Append(checkpoint.CreatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("id: ").Append(checkpoint.Id.ToString("D")).Append('\n');
        text.Append("hash: ").Append(checkpoint.Hash).Append('\n');
        text.Append('\n');

        foreach (var signature in signatures)
        {
            text.Append("— ").Append(signature.Scheme).Append(' ').Append(signature.Value).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads a checkpoint from C2SP checkpoint text.
    /// </summary>
    /// <param name="text">The checkpoint text to read.</param>
    /// <returns>The checkpoint the text describes.</returns>
    /// <exception cref="FormatException"><paramref name="text" /> is not valid checkpoint text.</exception>
    /// <remarks>
    ///     Text without LiteBus extension lines still reads: the origin becomes the segment, the tree size becomes the
    ///     count and the sequence, and the root hash becomes the hash, so a checkpoint a witness cosigned compares
    ///     against the chain without LiteBus fields.
    /// </remarks>
    public static AuditCheckpoint Parse(string text)
    {
        if (!TryParse(text, out var checkpoint) || checkpoint is null)
        {
            throw new FormatException("The text is not a valid transparency-log checkpoint.");
        }

        return checkpoint;
    }

    /// <summary>
    ///     Reads a checkpoint from C2SP checkpoint text without throwing on invalid input.
    /// </summary>
    /// <param name="text">The checkpoint text to read, or <see langword="null" />.</param>
    /// <param name="checkpoint">The checkpoint the text describes, or <see langword="null" /> when it is invalid.</param>
    /// <returns><see langword="true" /> when the text read; otherwise, <see langword="false" />.</returns>
    public static bool TryParse(string? text, out AuditCheckpoint? checkpoint)
    {
        checkpoint = null;

        if (string.IsNullOrEmpty(text) || !text.EndsWith('\n'))
        {
            return false;
        }

        if (text.Contains('\r'))
        {
            return false;
        }

        var lines = text.Split('\n')[..^1];
        var separator = -1;

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (lines[index].Length == 0)
            {
                separator = index;
                break;
            }
        }

        if (separator < 3)
        {
            return false;
        }

        for (var index = 0; index < separator; index++)
        {
            if (lines[index].Length == 0 || ContainsControl(lines[index]))
            {
                return false;
            }
        }

        List<AuditCheckpointSignature> signatures = [];

        for (var index = separator + 1; index < lines.Length; index++)
        {
            if (!TryParseSignature(lines[index], out var signature) || signature is null)
            {
                return false;
            }

            signatures.Add(signature);
        }

        var segment = lines[0];

        if (!TryParseCount(lines[1], out var count))
        {
            return false;
        }

        if (!TryDecodeBase64(lines[2], out var rootBytes) || rootBytes is null)
        {
            return false;
        }

        var extensions = ReadExtensions(lines, separator);

        if (!TryReadCheckpoint(segment, count, rootBytes, extensions, signatures, out checkpoint))
        {
            checkpoint = null;
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Selects the signatures rendering writes, preferring the list over the single legacy signature.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to render.</param>
    /// <returns>The signatures to write, possibly empty.</returns>
    private static IReadOnlyList<AuditCheckpointSignature> SelectSignatures(AuditCheckpoint checkpoint)
    {
        if (checkpoint.Signatures.Count > 0)
        {
            return checkpoint.Signatures;
        }

        if (checkpoint.Signature is not null)
        {
            return [checkpoint.Signature];
        }

        return [];
    }

    /// <summary>
    ///     Validates one signature before rendering writes it.
    /// </summary>
    /// <param name="signature">The signature to validate.</param>
    /// <exception cref="ArgumentException">The scheme is empty or the value is not canonical base64.</exception>
    private static void ValidateSignature(AuditCheckpointSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (string.IsNullOrWhiteSpace(signature.Scheme) ||
            signature.Scheme.Contains(' ') ||
            signature.Scheme.Contains('\t') ||
            signature.Scheme.Contains('+') ||
            signature.Scheme.Contains('\r') ||
            signature.Scheme.Contains('\n'))
        {
            throw new ArgumentException(
                "A checkpoint signature scheme must be non-empty and must not contain spaces, plus signs, or line breaks.",
                nameof(signature));
        }

        if (string.IsNullOrWhiteSpace(signature.Value) || !IsCanonicalBase64(signature.Value))
        {
            throw new ArgumentException(
                "A checkpoint signature value must be canonical base64.",
                nameof(signature));
        }
    }

    /// <summary>
    ///     Encodes a stored hash as the base64 root hash line.
    /// </summary>
    /// <param name="hash">The stored hash, hexadecimal from the LiteBus hashers.</param>
    /// <returns>The base64 root hash.</returns>
    private static string ToRootHash(string hash)
    {
        if (TryDecodeHex(hash, out var bytes) && bytes is not null)
        {
            return Convert.ToBase64String(bytes);
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(hash));
    }

    /// <summary>
    ///     Decides whether text holds an ASCII control character.
    /// </summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true" /> when a character below U+0020 is present.</returns>
    private static bool ContainsControl(string value)
    {
        foreach (var character in value)
        {
            if (character < 0x20)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Reads one signed-note signature line.
    /// </summary>
    /// <param name="line">The line after the separating blank line.</param>
    /// <param name="signature">The signature, or <see langword="null" /> when the line is invalid.</param>
    /// <returns><see langword="true" /> when the line read; otherwise, <see langword="false" />.</returns>
    private static bool TryParseSignature(string line, out AuditCheckpointSignature? signature)
    {
        signature = null;

        if (string.IsNullOrEmpty(line) || ContainsControl(line))
        {
            return false;
        }

        if (!line.StartsWith("— ", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = line.Substring(2);
        var separator = rest.IndexOf(' ');

        if (separator <= 0 || separator == rest.Length - 1)
        {
            return false;
        }

        var name = rest[..separator];
        var encoded = rest[(separator + 1)..];

        if (name.Contains('+') || !IsCanonicalBase64(encoded))
        {
            return false;
        }

        if (!TryDecodeBase64(encoded, out var bytes) || bytes is null || bytes.Length < 5)
        {
            return false;
        }

        signature = new AuditCheckpointSignature(name, encoded);

        return true;
    }

    /// <summary>
    ///     Reads the tree size line as a document count.
    /// </summary>
    /// <param name="value">The second line of the checkpoint.</param>
    /// <param name="count">The count, or zero when the line is invalid.</param>
    /// <returns><see langword="true" /> when the line read; otherwise, <see langword="false" />.</returns>
    private static bool TryParseCount(string value, out long count)
    {
        count = 0;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }
        }

        if (value.Length > 1 && value[0] == '0')
        {
            return false;
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count);
    }

    /// <summary>
    ///     Reads the extension lines into keyed values, ignoring lines from newer writers.
    /// </summary>
    /// <param name="lines">Every line of the note without the trailing newline.</param>
    /// <param name="separator">The index of the separating blank line.</param>
    /// <returns>The extension values by key.</returns>
    private static Dictionary<string, string> ReadExtensions(string[] lines, int separator)
    {
        Dictionary<string, string> extensions = new(StringComparer.Ordinal);

        for (var index = 3; index < separator; index++)
        {
            var line = lines[index];
            var colon = line.IndexOf(": ", StringComparison.Ordinal);

            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon];
            var value = line[(colon + 2)..];

            extensions[key] = value;
        }

        return extensions;
    }

    /// <summary>
    ///     Builds the checkpoint from its parsed lines and extensions.
    /// </summary>
    /// <param name="segment">The origin line.</param>
    /// <param name="count">The tree size.</param>
    /// <param name="rootBytes">The decoded root hash.</param>
    /// <param name="extensions">The extension values by key.</param>
    /// <param name="signatures">The parsed signatures.</param>
    /// <param name="checkpoint">The checkpoint, or <see langword="null" /> when an extension is invalid.</param>
    /// <returns><see langword="true" /> when the checkpoint built; otherwise, <see langword="false" />.</returns>
    private static bool TryReadCheckpoint(
        string segment,
        long count,
        byte[] rootBytes,
        Dictionary<string, string> extensions,
        List<AuditCheckpointSignature> signatures,
        out AuditCheckpoint? checkpoint)
    {
        checkpoint = null;

        var sequence = count;

        if (extensions.TryGetValue("sequence", out var sequenceValue))
        {
            if (!TryParseCount(sequenceValue, out sequence))
            {
                return false;
            }
        }

        var scheme = UnknownSchemeName;

        if (extensions.TryGetValue("scheme", out var schemeValue))
        {
            if (string.IsNullOrEmpty(schemeValue))
            {
                return false;
            }

            scheme = schemeValue;
        }

        var kind = AuditCheckpointKind.Regular;

        if (extensions.TryGetValue("kind", out var kindValue))
        {
            if (kindValue is not "Regular" and not "Final")
            {
                return false;
            }

            kind = kindValue == "Final" ? AuditCheckpointKind.Final : AuditCheckpointKind.Regular;
        }

        string? label = null;

        if (extensions.TryGetValue("label", out var labelValue) && labelValue.Length > 0)
        {
            label = labelValue;
        }

        var createdAt = DateTimeOffset.UtcNow;

        if (extensions.TryGetValue("created", out var createdValue))
        {
            if (!DateTimeOffset.TryParse(createdValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out createdAt))
            {
                return false;
            }
        }

        var id = Guid.NewGuid();

        if (extensions.TryGetValue("id", out var idValue))
        {
            if (!Guid.TryParse(idValue, out id))
            {
                return false;
            }
        }

        var hash = extensions.TryGetValue("hash", out var hashValue) && hashValue.Length > 0
            ? hashValue
            : Convert.ToHexStringLower(rootBytes);

        AuditCheckpoint parsed = new(id, segment, sequence, count, hash, scheme, kind, createdAt)
        {
            Label = label,
            PublishState = CheckpointPublishState.Published,
            Signatures = signatures,
            Signature = signatures.Count == 0 ? null : signatures[0],
        };

        checkpoint = parsed;

        return true;
    }

    /// <summary>
    ///     Decides whether text is canonical base64 with correct padding.
    /// </summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true" /> when the text is canonical base64.</returns>
    private static bool IsCanonicalBase64(string value)
    {
        if (value.Length == 0 || value.Length % 4 != 0)
        {
            return false;
        }

        var padding = 0;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isBase64 = (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '+' ||
                character == '/';

            if (isBase64)
            {
                if (padding > 0)
                {
                    return false;
                }

                continue;
            }

            if (character != '=')
            {
                return false;
            }

            padding++;

            if (padding > 2)
            {
                return false;
            }
        }

        if (!TryDecodeBase64(value, out var bytes) || bytes is null)
        {
            return false;
        }

        return string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Decodes base64 without throwing on invalid input.
    /// </summary>
    /// <param name="value">The text to decode.</param>
    /// <param name="bytes">The decoded bytes, or <see langword="null" /> when the text is invalid.</param>
    /// <returns><see langword="true" /> when the text decoded; otherwise, <see langword="false" />.</returns>
    private static bool TryDecodeBase64(string value, out byte[]? bytes)
    {
        bytes = null;

        try
        {
            bytes = Convert.FromBase64String(value);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Decodes lowercase or uppercase hexadecimal without throwing on invalid input.
    /// </summary>
    /// <param name="value">The text to decode.</param>
    /// <param name="bytes">The decoded bytes, or <see langword="null" /> when the text is not hexadecimal.</param>
    /// <returns><see langword="true" /> when the text decoded; otherwise, <see langword="false" />.</returns>
    private static bool TryDecodeHex(string value, out byte[]? bytes)
    {
        bytes = null;

        if (value.Length == 0 || value.Length % 2 != 0)
        {
            return false;
        }

        byte[] decoded = new byte[value.Length / 2];

        for (var index = 0; index < decoded.Length; index++)
        {
            var high = HexValue(value[index * 2]);
            var low = HexValue(value[(index * 2) + 1]);

            if (high < 0 || low < 0)
            {
                return false;
            }

            decoded[index] = (byte) ((high << 4) | low);
        }

        bytes = decoded;

        return true;
    }

    /// <summary>
    ///     Reads one hexadecimal character as its value.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The value from zero to fifteen, or negative one when the character is not hexadecimal.</returns>
    private static int HexValue(char character)
    {
        if (character >= '0' && character <= '9')
        {
            return character - '0';
        }

        if (character >= 'a' && character <= 'f')
        {
            return character - 'a' + 10;
        }

        if (character >= 'A' && character <= 'F')
        {
            return character - 'A' + 10;
        }

        return -1;
    }
}
