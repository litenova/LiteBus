namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Decides when a chain segment is due for a checkpoint.
/// </summary>
/// <remarks>
///     A policy can count documents, measure time since the last checkpoint, or combine both. The chain asks it after
///     every append, and a time-based policy is also asked on a schedule so a quiet segment still gets its checkpoint.
/// </remarks>
public interface ICheckpointPolicy
{
    /// <summary>
    ///     Gets a value indicating whether the policy can become due by time alone, without a new append.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> for a policy that measures time, so the chain also asks it on a schedule. The default
    ///     is <see langword="false" />.
    /// </value>
    bool IsTimeBased => false;

    /// <summary>
    ///     Returns whether a checkpoint is due for the segment.
    /// </summary>
    /// <param name="candidate">The current state of the segment.</param>
    /// <returns><see langword="true" /> when a checkpoint should be cut now.</returns>
    bool IsDue(AuditCheckpointCandidate candidate);
}
