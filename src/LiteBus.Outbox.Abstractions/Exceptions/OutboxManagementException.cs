using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Thrown when an outbox management operation violates operator safety rules.
/// </summary>
public sealed class OutboxManagementException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxManagementException" /> class.
    /// </summary>
    /// <param name="message">The error message that explains why the operation was rejected.</param>
    public OutboxManagementException(string message)
        : base(message)
    {
    }
}
