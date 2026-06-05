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
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this mapping failure.</param>
    public TransportHeaderMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
