using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when LiteBus module registration or runtime composition is invalid.
/// </summary>
public sealed class LiteBusConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public LiteBusConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public LiteBusConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}