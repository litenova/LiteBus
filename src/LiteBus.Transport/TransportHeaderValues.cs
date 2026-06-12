namespace LiteBus.Transport;

/// <summary>
///     Forwards to <see cref="Abstractions.TransportHeaderValues" /> for backward-compatible call sites.
/// </summary>
[Obsolete("Use LiteBus.Transport.Abstractions.TransportHeaderValues instead.")]
public static class TransportHeaderValues
{
    /// <summary>
    ///     Reads a header value as a string.
    /// </summary>
    /// <param name="headers">The header dictionary from a received message.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The string value, or <see langword="null" /> when the header is absent.</returns>
    public static string? GetString(IReadOnlyDictionary<string, object?> headers, string name)
    {
        return Abstractions.TransportHeaderValues.GetString(headers, name);
    }

    /// <summary>
    ///     Reads a header value as a 32-bit integer.
    /// </summary>
    /// <param name="headers">The header dictionary from a received message.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The integer value, or <see langword="null" /> when the header is absent or not numeric.</returns>
    public static int? GetInt32(IReadOnlyDictionary<string, object?> headers, string name)
    {
        return Abstractions.TransportHeaderValues.GetInt32(headers, name);
    }

    /// <summary>
    ///     Converts a transport header value to a string when possible.
    /// </summary>
    /// <param name="value">The raw header value from the broker.</param>
    /// <returns>The string representation, or <see langword="null" /> when the value is absent.</returns>
    public static string? ConvertToString(object? value)
    {
        return Abstractions.TransportHeaderValues.ConvertToString(value);
    }
}
