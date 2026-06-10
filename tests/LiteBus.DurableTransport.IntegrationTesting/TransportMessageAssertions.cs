using System.Text;
using LiteBus.Transport.Abstractions;

namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Assertion helpers for transport messages observed in integration tests.
/// </summary>
public static class TransportMessageAssertions
{
    /// <summary>
    ///     Reads the transport message body as UTF-8 text.
    /// </summary>
    /// <param name="message">The received transport message.</param>
    /// <returns>The decoded message body.</returns>
    public static string ReadBody(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Encoding.UTF8.GetString(message.Body.Span);
    }

    /// <summary>
    ///     Gets a required string header value from a transport message.
    /// </summary>
    /// <param name="message">The received transport message.</param>
    /// <param name="headerName">The header name to read.</param>
    /// <returns>The header value as a string.</returns>
    public static string GetHeader(TransportMessage message, string headerName)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Headers.TryGetValue(headerName, out var value);
        return value?.ToString() ?? string.Empty;
    }
}
