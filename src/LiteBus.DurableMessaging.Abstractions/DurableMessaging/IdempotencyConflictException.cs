using System;

namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Thrown when a durable writer rejects a duplicate idempotency key or message identifier under
///     <see cref="IdempotencyConflictMode.Strict" />.
/// </summary>
public sealed class IdempotencyConflictException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyConflictException" /> class.
    /// </summary>
    /// <param name="message">The error message that explains the conflict.</param>
    public IdempotencyConflictException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyConflictException" /> class.
    /// </summary>
    /// <param name="message">The error message that explains the conflict.</param>
    /// <param name="innerException">The exception that caused the conflict detection.</param>
    public IdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
