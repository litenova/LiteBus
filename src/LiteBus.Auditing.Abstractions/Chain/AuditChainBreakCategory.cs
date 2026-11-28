namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Which question a chain break answers: whether the chain was altered, or whether checkpoint coverage is current.
/// </summary>
public enum AuditChainBreakCategory
{
    /// <summary>
    ///     The chain or a checkpoint was altered or does not match what it should.
    /// </summary>
    Integrity = 0,

    /// <summary>
    ///     A checkpoint, publication, or link has not happened yet or stopped happening.
    /// </summary>
    Coverage = 1
}
