using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when a requested LiteBus operation is not supported in the current environment or configuration.
/// </summary>
public sealed class LiteBusNotSupportedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusNotSupportedException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public LiteBusNotSupportedException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusNotSupportedException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public LiteBusNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}