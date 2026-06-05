using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Accumulates per-pass outcomes during a single processor iteration.
/// </summary>
/// <typeparam name="TEnvelope">The envelope type collected for persistence.</typeparam>
/// <remarks>
///     Collects post-transition envelope instances ready for a single store round trip.
///     Not thread-safe; one instance per processor pass.
/// </remarks>
public sealed class ProcessorPassAccumulator<TEnvelope>
{
    /// <summary>
    ///     The post-transition envelopes to be persisted in one store call.
    /// </summary>
    private readonly List<TEnvelope> _updates = [];

    /// <summary>
    ///     The number of envelopes that completed successfully during the pass.
    /// </summary>
    private int _succeededCount;

    /// <summary>
    ///     The number of envelopes marked failed for retry during the pass.
    /// </summary>
    private int _failedCount;

    /// <summary>
    ///     The number of envelopes moved to dead-letter state during the pass.
    /// </summary>
    private int _deadLetteredCount;

    /// <summary>
    ///     Gets the post-transition envelopes to be persisted in one store call.
    /// </summary>
    public IReadOnlyList<TEnvelope> Updates => _updates;

    /// <summary>
    ///     Gets the number of envelopes that completed successfully during the pass.
    /// </summary>
    public int SucceededCount => _succeededCount;

    /// <summary>
    ///     Gets the number of envelopes marked failed for retry during the pass.
    /// </summary>
    public int FailedCount => _failedCount;

    /// <summary>
    ///     Gets the number of envelopes moved to dead-letter state during the pass.
    /// </summary>
    public int DeadLetteredCount => _deadLetteredCount;

    /// <summary>
    ///     Gets the total number of post-transition envelopes collected during the pass.
    /// </summary>
    public int TotalCount => _updates.Count;

    /// <summary>
    ///     Records one successful envelope outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    public void RecordSucceeded(TEnvelope envelope)
    {
        _updates.Add(envelope);
        _succeededCount++;
    }

    /// <summary>
    ///     Records one retryable failure outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    public void RecordFailed(TEnvelope envelope)
    {
        _updates.Add(envelope);
        _failedCount++;
    }

    /// <summary>
    ///     Records one dead-letter outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    public void RecordDeadLettered(TEnvelope envelope)
    {
        _updates.Add(envelope);
        _deadLetteredCount++;
    }

    /// <summary>
    ///     Builds the pass result from the accumulated outcomes.
    /// </summary>
    /// <param name="leasedCount">The number of envelopes leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <returns>The processor pass result.</returns>
    public ProcessorPassResult ToResult(int leasedCount, TimeSpan elapsed) =>
        new()
        {
            LeasedCount = leasedCount,
            SucceededCount = _succeededCount,
            FailedCount = _failedCount,
            DeadLetteredCount = _deadLetteredCount,
            ElapsedTime = elapsed
        };
}
