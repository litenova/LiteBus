namespace LiteBus.Transport.AwsSqs.Exceptions;

/// <summary>
///     Thrown when AWS SQS transport configuration or client state is invalid.
/// </summary>
public sealed class AwsSqsTransportConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsTransportConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AwsSqsTransportConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsTransportConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public AwsSqsTransportConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
