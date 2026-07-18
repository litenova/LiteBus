namespace LiteBus.Inbox.Ingress.Amqp.Exceptions;

/// <summary>
///     Thrown when AMQP inbox ingress is misconfigured or required services are missing.
/// </summary>
public sealed class AmqpInboxIngressConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxIngressConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AmqpInboxIngressConfigurationException(string message)
        : base(message)
    {
    }
}