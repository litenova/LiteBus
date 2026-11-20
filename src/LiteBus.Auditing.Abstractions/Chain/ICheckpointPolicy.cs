using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Abstractions;

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

    /// <summary>
    ///     Returns whether a checkpoint is due for the segment, with access to services.
    /// </summary>
    /// <param name="candidate">The current state of the segment.</param>
    /// <param name="cancellationToken">The token used to cancel the evaluation.</param>
    /// <returns><see langword="true" /> when a checkpoint should be cut now.</returns>
    /// <remarks>
    ///     The default implementation calls <see cref="IsDue" />, so a policy that needs no services keeps working
    ///     unchanged. A policy that reads services, identity, or tenant metadata overrides this method; it still
    ///     implements <see cref="IsDue" /> for callers that evaluate synchronously.
    /// </remarks>
    ValueTask<bool> IsDueAsync(AuditCheckpointCandidate candidate, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(IsDue(candidate));
    }
}
