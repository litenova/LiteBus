using System.Collections.Generic;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Reads typed values from AMQP application headers.
/// </summary>
/// <remarks>
///     Delegates to <see cref="TransportHeaderValues" /> so AMQP and transport-neutral call sites share parsing logic.
/// </remarks>
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
        return TransportHeaderValues.GetString(headers, name);
    }

    /// <summary>
    ///     Reads a header value as a 32-bit integer.
    /// </summary>
    /// <param name="headers">The header dictionary from a received message.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The integer value, or <see langword="null" /> when the header is absent or not numeric.</returns>
    public static int? GetInt32(IReadOnlyDictionary<string, object?> headers, string name)
    {
        return TransportHeaderValues.GetInt32(headers, name);
    }

    /// <summary>
    ///     Converts an AMQP header value to a string when possible.
    /// </summary>
    /// <param name="value">The raw header value from the broker.</param>
    /// <returns>The string representation, or <see langword="null" /> when the value is absent.</returns>
    internal static string? ConvertToString(object? value)
    {
        return TransportHeaderValues.ConvertToString(value);
    }
}
