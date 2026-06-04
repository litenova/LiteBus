using System;

namespace LiteBus.Inbox.Storage.InMemory.Exceptions;

/// <summary>
///     Thrown when the in-memory inbox store rejects an operation.
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
