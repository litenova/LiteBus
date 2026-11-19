using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Decides when a captured audit record is assigned its chain position: the sequence, the previous hash, and the
///     hash.
/// </summary>
/// <remarks>
///     Linking applies only with the hash chain on. Valid combinations with <see cref="AuditCapture" /> are
///     <c>WithChange</c> with <see cref="AfterCommit" /> (the default) or <see cref="InTransaction" />, while
///     <c>AfterCommitThroughOutbox</c> and <c>BeforeAndOutcome</c> always link <see cref="AtWrite" />. Composition
///     rejects any other pair with a configuration exception.
/// </remarks>
public enum AuditLink
{
    /// <summary>
    ///     Records commit unlinked; a linker assigns positions in commit order right after commit, with a background
    ///     sweep for crash leftovers. Verification reports an <c>Unlinked</c> break past a configured age.
    /// </summary>
    /// <remarks>
    ///     No contention on the business path: the business transaction never touches the segment head.
    /// </remarks>
    AfterCommit = 0,

    /// <summary>
    ///     The segment head is updated inside the business transaction.
    /// </summary>
    /// <remarks>
    ///     Every audited command in a segment contends on one row. Use this only where the contention is acceptable.
    /// </remarks>
    InTransaction = 1,

    /// <summary>
    ///     Whoever writes the record links it in its own transaction.
    /// </summary>
    /// <remarks>
    ///     Natural when the audit store is a separate system that the business transaction cannot join.
    /// </remarks>
    AtWrite = 2
}
