using System;
using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Thread-safe accumulator for processor pass outcomes when multiple workers record results in parallel.
/// </summary>
/// <typeparam name="TEnvelope">The envelope type collected for optional batch persistence.</typeparam>
/// <remarks>
///     Used by pipelined processors where dispatch workers run concurrently. Count fields use interlocked
///     increments; the updates list is guarded by a lock when callers need deferred persistence.
/// </remarks>
public sealed class ConcurrentProcessorPassAccumulator<TEnvelope> : IProcessorPassRecorder<TEnvelope>
{
    /// <summary>
    ///     The lock that serializes access to the optional deferred update list.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The post-transition envelopes collected for optional batch persistence.
    /// </summary>
    private readonly List<TEnvelope> _updates = [];

    /// <summary>
    ///     The number of envelopes moved to dead-letter state during the pass.
    /// </summary>
    private int _deadLetteredCount;

    /// <summary>
    ///     The number of envelopes marked failed for retry during the pass.
    /// </summary>
    private int _failedCount;

    /// <summary>
    ///     The number of envelopes that completed successfully during the pass.
    /// </summary>
    private int _succeededCount;

    /// <summary>
    ///     Gets the post-transition envelopes collected for optional batch persistence.
    /// </summary>
    public IReadOnlyList<TEnvelope> Updates
    {
        get
        {
            lock (_sync)
            {
                return [.. _updates];
            }
        }
    }

    /// <summary>
    ///     Gets the number of envelopes that completed successfully during the pass.
    /// </summary>
    public int SucceededCount => Volatile.Read(ref _succeededCount);

    /// <summary>
    ///     Gets the number of envelopes marked failed for retry during the pass.
    /// </summary>
    public int FailedCount => Volatile.Read(ref _failedCount);

    /// <summary>
    ///     Gets the number of envelopes moved to dead-letter state during the pass.
    /// </summary>
    public int DeadLetteredCount => Volatile.Read(ref _deadLetteredCount);

    /// <summary>
    ///     Gets the total number of post-transition envelopes collected during the pass.
    /// </summary>
    public int TotalCount
    {
        get
        {
            lock (_sync)
            {
                return _updates.Count;
            }
        }
    }

    /// <summary>
    ///     Records one successful envelope outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    public void RecordSucceeded(TEnvelope envelope)
    {
        lock (_sync)
        {
            _updates.Add(envelope);
        }

        Interlocked.Increment(ref _succeededCount);
    }

    /// <summary>
    ///     Records one retryable failure outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    public void RecordFailed(TEnvelope envelope)
    {
        lock (_sync)
        {
            _updates.Add(envelope);
        }

        Interlocked.Increment(ref _failedCount);
    }

    /// <summary>
    ///     Records one dead-letter outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    public void RecordDeadLettered(TEnvelope envelope)
    {
        lock (_sync)
        {
            _updates.Add(envelope);
        }

        Interlocked.Increment(ref _deadLetteredCount);
    }

    /// <summary>
    ///     Builds the pass result from the accumulated outcomes.
    /// </summary>
    /// <param name="leasedCount">The number of envelopes leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <returns>The processor pass result.</returns>
    public ProcessorPassResult ToResult(int leasedCount, TimeSpan elapsed)
    {
        return new ProcessorPassResult
        {
            LeasedCount = leasedCount,
            SucceededCount = SucceededCount,
            FailedCount = FailedCount,
            DeadLetteredCount = DeadLetteredCount,
            ElapsedTime = elapsed
        };
    }
}