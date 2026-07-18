using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Thrown when an inbox management operation violates operator safety rules.
/// </summary>
public sealed class InboxManagementException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxManagementException" /> class.
    /// </summary>
    /// <param name="message">The error message that explains why the operation was rejected.</param>
    public InboxManagementException(string message)
        : base(message)
    {
    }
}