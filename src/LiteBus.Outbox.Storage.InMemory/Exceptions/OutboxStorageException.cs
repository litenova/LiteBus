using System;

namespace LiteBus.Outbox.Storage.InMemory.Exceptions;

/// <summary>
///     Thrown when the in-memory outbox store rejects an operation.
/// </summary>
public sealed class OutboxStorageException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxStorageException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public OutboxStorageException(string message)
        : base(message)
    {
    }
}