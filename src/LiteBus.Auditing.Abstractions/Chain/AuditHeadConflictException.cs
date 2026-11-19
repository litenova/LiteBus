using System;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Thrown when an audit chain head changed between read and write, so a conditional append lost the race.
/// </summary>
/// <remarks>
///     The linker and the sweep catch this, re-read the head, and retry. A caller that sees it outside those paths
///     raced another writer on the same segment.
/// </remarks>
public sealed class AuditHeadConflictException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditHeadConflictException" /> class.
    /// </summary>
    /// <param name="message">The message describing which segment raced.</param>
    public AuditHeadConflictException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditHeadConflictException" /> class.
    /// </summary>
    /// <param name="message">The message describing which segment raced.</param>
    /// <param name="innerException">The store exception that reported the version mismatch.</param>
    public AuditHeadConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
