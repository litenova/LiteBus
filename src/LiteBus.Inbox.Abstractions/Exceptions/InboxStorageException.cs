using System;

namespace LiteBus.Inbox.Abstractions.Exceptions;

/// <summary>
///     Thrown when an inbox store rejects an accept or persistence operation.
/// </summary>
public sealed class InboxStorageException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxStorageException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InboxStorageException(string message)
        : base(message)
    {
    }
}
