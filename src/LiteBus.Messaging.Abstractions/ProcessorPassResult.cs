using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes the outcome of one inbox or outbox processor pass.
/// </summary>
/// <remarks>
///     <para>
///         A pass leases a bounded batch and processes each leased envelope before returning. The leased count helps
///         hosted processors apply adaptive polling when a full batch indicates more work may be waiting.
///     </para>
/// </remarks>
public sealed record ProcessorPassResult
{
    /// <summary>
    ///     Gets the number of commands or messages leased and processed during the pass.
    /// </summary>
    public required int LeasedCount { get; init; }

    /// <summary>
    ///     Gets the number of leased envelopes that completed successfully during the pass.
    /// </summary>
    public int SucceededCount { get; init; }

    /// <summary>
    ///     Gets the number of leased envelopes that were marked failed for retry during the pass.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    ///     Gets the number of leased envelopes that were moved to dead-letter state during the pass.
    /// </summary>
    public int DeadLetteredCount { get; init; }

    /// <summary>
    ///     Gets the wall-clock duration of the pass, including leasing and state updates.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }
}