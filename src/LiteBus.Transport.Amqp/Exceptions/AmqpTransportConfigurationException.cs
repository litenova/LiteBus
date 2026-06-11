using System;

namespace LiteBus.Transport.Amqp.Exceptions;

/// <summary>
///     Thrown when AMQP transport configuration or lifecycle state is invalid.
/// </summary>
public sealed class AmqpTransportConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpTransportConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AmqpTransportConfigurationException(string message)
        : base(message)
    {
    }
}