using System;

namespace LiteBus.Inbox.Abstractions.Exceptions;

/// <summary>
///     Thrown when transport inbox ingress cannot map, authorize, or accept a broker delivery.
/// </summary>
public sealed class InboxIngressException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxIngressException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InboxIngressException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxIngressException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InboxIngressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
