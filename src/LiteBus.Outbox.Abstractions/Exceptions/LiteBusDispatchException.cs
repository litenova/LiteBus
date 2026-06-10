using System;

namespace LiteBus.Outbox.Abstractions.Exceptions;

/// <summary>
///     Thrown when outbox dispatch cannot publish or replay a leased envelope.
/// </summary>
public sealed class LiteBusDispatchException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusDispatchException" /> class.
    /// </summary>
    /// <param name="message">The error message describing the failure and remediation.</param>
    public LiteBusDispatchException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusDispatchException" /> class.
    /// </summary>
    /// <param name="message">The error message describing the failure and remediation.</param>
    /// <param name="innerException">The exception that caused this dispatch failure.</param>
    public LiteBusDispatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
