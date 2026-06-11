using System;

namespace LiteBus.Inbox.Abstractions.Exceptions;

/// <summary>
///     Thrown when inbox dispatch or ingress cannot accept or route a message.
/// </summary>
public sealed class InboxDispatchException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxDispatchException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InboxDispatchException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxDispatchException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InboxDispatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}