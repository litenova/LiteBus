using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LiteBus.Amqp;

/// <summary>
///     Reads typed values from AMQP application headers.
/// </summary>
public static class AmqpHeaderValues
{
    /// <summary>
    ///     Reads a header value as a string.
    /// </summary>
    /// <param name="headers">The header dictionary from a received message.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The string value, or <see langword="null" /> when the header is absent.</returns>
    public static string? GetString(IReadOnlyDictionary<string, object?> headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!headers.TryGetValue(name, out var value))
        {
            return null;
        }

        return ToString(value);
    }

    /// <summary>
    ///     Reads a header value as a 32-bit integer.
    /// </summary>
    /// <param name="headers">The header dictionary from a received message.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The integer value, or <see langword="null" /> when the header is absent or not numeric.</returns>
    public static int? GetInt32(IReadOnlyDictionary<string, object?> headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!headers.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int number => number,
            byte singleByte => singleByte,
            sbyte signedByte => signedByte,
            short number => number,
            long number when number >= int.MinValue && number <= int.MaxValue => (int)number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            byte[] bytes when bytes.Length <= 4 && TryParseBytesAsInt32(bytes, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    ///     Converts an AMQP header value to a string when possible.
    /// </summary>
    /// <param name="value">The raw header value from the broker.</param>
    /// <returns>The string representation, or <see langword="null" /> when the value is absent.</returns>
    internal static string? ToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            Memory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    ///     Parses a little-endian integer encoded in a byte array header value.
    /// </summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <param name="value">The parsed integer when conversion succeeds.</param>
    /// <returns><see langword="true" /> when the bytes represent an integer; otherwise <see langword="false" />.</returns>
    private static bool TryParseBytesAsInt32(byte[] bytes, out int value)
    {
        if (bytes.Length == 1)
        {
            value = bytes[0];
            return true;
        }

        if (bytes.Length == 4)
        {
            value = BitConverter.ToInt32(bytes, 0);
            return true;
        }

        value = default;
        return false;
    }
}
