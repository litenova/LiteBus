using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Accumulates per-pass outcome counts during a single outbox processor iteration.
/// </summary>
/// <remarks>
///     Not thread-safe; one instance per processor pass.
/// </remarks>
internal sealed class ProcessorPassAccumulator
{
    /// <summary>
    ///     The identifiers of envelopes that published successfully during the pass.
    /// </summary>
    private readonly List<Guid> _succeededIds = [];

    /// <summary>
    ///     The failures collected for batch retry updates.
    /// </summary>
    private readonly List<OutboxEnvelopeFailure> _failures = [];

    /// <summary>
    ///     The dead-letter transitions collected during the pass.
    /// </summary>
    private readonly List<OutboxEnvelopeDeadLetter> _deadLetters = [];

    /// <summary>
    ///     Gets the identifiers of envelopes that published successfully during the pass.
    /// </summary>
    public IReadOnlyList<Guid> SucceededIds => _succeededIds;

    /// <summary>
    ///     Gets the failures collected for batch retry updates.
    /// </summary>
    public IReadOnlyList<OutboxEnvelopeFailure> Failures => _failures;

    /// <summary>
    ///     Gets the dead-letter transitions collected during the pass.
    /// </summary>
    public IReadOnlyList<OutboxEnvelopeDeadLetter> DeadLetters => _deadLetters;

    /// <summary>
    ///     Records one successful envelope outcome.
    /// </summary>
    /// <param name="messageId">The identifier of the envelope that published.</param>
    public void RecordSuccess(Guid messageId) =>
        _succeededIds.Add(messageId);

    /// <summary>
    ///     Records one retryable failure outcome.
    /// </summary>
    /// <param name="failure">The failure details to persist.</param>
    public void RecordFailure(OutboxEnvelopeFailure failure) =>
        _failures.Add(failure);

    /// <summary>
    ///     Records one dead-letter outcome.
    /// </summary>
    /// <param name="deadLetter">The dead-letter details to persist.</param>
    public void RecordDeadLetter(OutboxEnvelopeDeadLetter deadLetter) =>
        _deadLetters.Add(deadLetter);

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
            SucceededCount = _succeededIds.Count,
            FailedCount = _failures.Count,
            DeadLetteredCount = _deadLetters.Count,
            ElapsedTime = elapsed
        };
}
