namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Thrown when a transport delivery is missing required LiteBus header metadata.
/// </summary>
public sealed class TransportHeaderMappingException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportHeaderMappingException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TransportHeaderMappingException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportHeaderMappingException" /> class.
    /// </summary>
    /// <param name="headerName">The header name that was missing or invalid.</param>
    /// <param name="message">The error message.</param>
    public TransportHeaderMappingException(string headerName, string message)
        : base(message)
    {
        HeaderName = headerName;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportHeaderMappingException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this mapping failure.</param>
    public TransportHeaderMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Gets the transport header name that was missing or invalid, when known.
    /// </summary>
    public string? HeaderName { get; }

    /// <summary>
    ///     Creates an exception for a required header that the publisher did not supply.
    /// </summary>
    /// <param name="headerName">The required header name.</param>
    /// <returns>A mapping exception naming the header and publisher responsibility.</returns>
    public static TransportHeaderMappingException MissingRequiredHeader(string headerName)
    {
        return new TransportHeaderMappingException(
            headerName,
            $"Transport header '{headerName}' is required but missing or empty on the delivery. " +
            $"Publishers must set '{headerName}' when sending through IMessageTransport, " +
            "TransportOutboxDispatcher, TransportInboxDispatcher, or an equivalent transport adapter.");
    }

    /// <summary>
    ///     Creates an exception for a required header whose value is invalid.
    /// </summary>
    /// <param name="headerName">The required header name.</param>
    /// <param name="detail">The validation detail describing the invalid value.</param>
    /// <returns>A mapping exception naming the header and publisher responsibility.</returns>
    public static TransportHeaderMappingException InvalidRequiredHeader(string headerName, string detail)
    {
        return new TransportHeaderMappingException(
            headerName,
            $"Transport header '{headerName}' is invalid: {detail} " +
            $"Publishers must set '{headerName}' correctly when sending through IMessageTransport, " +
            "TransportOutboxDispatcher, TransportInboxDispatcher, or an equivalent transport adapter.");
    }
}