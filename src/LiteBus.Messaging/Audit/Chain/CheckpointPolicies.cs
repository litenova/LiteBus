using System;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Builds the checkpoint policies an audit chain cuts checkpoints with.
/// </summary>
/// <remarks>
///     <para>
///         The policies are the replaceable <see cref="ICheckpointPolicy" /> port with LiteBus defaults: a chain asks
///         its policy after every append whether a checkpoint is due, and asks a time-based policy on a schedule as
///         well, so a quiet segment still gets its checkpoint. An application that checkpoints on its own rhythm, such
///         as a weekly close, implements <see cref="ICheckpointPolicy" /> and passes it instead.
///     </para>
/// </remarks>
public static class CheckpointPolicies
{
    /// <summary>
    ///     Returns a policy that is due every given number of appended records.
    /// </summary>
    /// <param name="recordCount">How many records are appended between checkpoints. Must be positive.</param>
    /// <returns>
    ///     A policy that is due once the segment has advanced the given number of sequences past its last checkpoint.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordCount" /> is zero or negative.</exception>
    public static ICheckpointPolicy EveryRecords(int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordCount, 1);

        return new RecordCountCheckpointPolicy(recordCount);
    }

    /// <summary>
    ///     Returns a policy that is due once the given time has passed since the last checkpoint.
    /// </summary>
    /// <param name="interval">How long after the last checkpoint the next one is due. Must be positive.</param>
    /// <returns>
    ///     A time-based policy that is due once the segment holds records and the interval has passed since the last
    ///     checkpoint.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    public static ICheckpointPolicy Every(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        return new TimeIntervalCheckpointPolicy(interval);
    }

    /// <summary>
    ///     Returns a policy that is due when any of the given policies is due.
    /// </summary>
    /// <param name="policies">The policies to combine. With none, a checkpoint is never due.</param>
    /// <returns>A policy that consults each given policy in order and is due when one of them is.</returns>
    /// <exception cref="ArgumentException"><paramref name="policies" /> contains a <see langword="null" /> entry.</exception>
    public static ICheckpointPolicy Any(params ICheckpointPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        foreach (var policy in policies)
        {
            if (policy is null)
            {
                throw new ArgumentException("The checkpoint policies must not contain a null entry.", nameof(policies));
            }
        }

        return new AnyCheckpointPolicy(policies);
    }

    /// <summary>
    ///     Cuts a checkpoint every fixed number of appended records.
    /// </summary>
    private sealed class RecordCountCheckpointPolicy : ICheckpointPolicy
    {
        /// <summary>
        ///     How many sequences past the last checkpoint make a checkpoint due. Fixed for the life of the policy.
        /// </summary>
        private readonly int _recordCount;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecordCountCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="recordCount">How many records are appended between checkpoints.</param>
        public RecordCountCheckpointPolicy(int recordCount)
        {
            _recordCount = recordCount;
        }

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return candidate.Sequence - (candidate.LastCheckpointSequence ?? 0) >= _recordCount;
        }
    }

    /// <summary>
    ///     Cuts a checkpoint once a fixed time has passed since the last checkpoint.
    /// </summary>
    private sealed class TimeIntervalCheckpointPolicy : ICheckpointPolicy
    {
        /// <summary>
        ///     How long after the last checkpoint the next one is due. Fixed for the life of the policy.
        /// </summary>
        private readonly TimeSpan _interval;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TimeIntervalCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="interval">How long after the last checkpoint the next one is due.</param>
        public TimeIntervalCheckpointPolicy(TimeSpan interval)
        {
            _interval = interval;
        }

        /// <inheritdoc />
        public bool IsTimeBased => true;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            if (candidate.Count <= 0)
            {
                return false;
            }

            if (candidate.LastCheckpointAt is null)
            {
                return true;
            }

            return candidate.Now - candidate.LastCheckpointAt.Value >= _interval;
        }
    }

    /// <summary>
    ///     Combines checkpoint policies so that any one of them can cut a checkpoint.
    /// </summary>
    private sealed class AnyCheckpointPolicy : ICheckpointPolicy
    {
        /// <summary>
        ///     The policies consulted in order. Copied at construction and never mutated afterwards.
        /// </summary>
        private readonly ICheckpointPolicy[] _policies;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnyCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="policies">The policies to combine.</param>
        public AnyCheckpointPolicy(ICheckpointPolicy[] policies)
        {
            _policies = [.. policies];
        }

        /// <inheritdoc />
        public bool IsTimeBased
        {
            get
            {
                foreach (var policy in _policies)
                {
                    if (policy.IsTimeBased)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            foreach (var policy in _policies)
            {
                if (policy.IsDue(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
