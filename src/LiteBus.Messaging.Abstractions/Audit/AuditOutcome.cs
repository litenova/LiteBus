namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes how an audited action ended.
/// </summary>
/// <remarks>
///     A denial is separated from a failure because they answer different questions. A denial means the actor was not
///     permitted to do this, which is what a security review asks about. A failure means the action was permitted and
///     went wrong, which is what an incident review asks about.
/// </remarks>
public enum AuditOutcome
{
    /// <summary>
    ///     The action completed.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    ///     The action was refused before it took effect.
    /// </summary>
    Denied = 1,

    /// <summary>
    ///     The action was permitted but did not complete.
    /// </summary>
    Failed = 2,

    /// <summary>
    ///     The action was cancelled before it completed.
    /// </summary>
    Canceled = 3,

    /// <summary>
    ///     The action was rejected because its input failed validation.
    /// </summary>
    /// <remarks>
    ///     Pairs with <see cref="MediationOutcome.Invalid" />. It is kept apart from <see cref="Denied" /> so that
    ///     malformed input does not appear in the list a security review reads.
    /// </remarks>
    Invalid = 4
}
