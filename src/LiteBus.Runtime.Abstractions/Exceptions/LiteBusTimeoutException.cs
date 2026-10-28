using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when a LiteBus operation exceeds its configured time limit.
/// </summary>
public sealed class LiteBusTimeoutException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusTimeoutException" /> class.
    /// </summary>
    /// <param name="message">The timeout error message.</param>
    public LiteBusTimeoutException(string message)
        : base(message)
    {
    }
}
