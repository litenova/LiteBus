using System;
using System.Diagnostics;

namespace LiteBus.Testing;

/// <summary>
///     Describes the aggregate outcome of draining an inbox or outbox processor until it goes idle.
/// </summary>
[DebuggerDisplay("Passes={Passes}, Succeeded={SucceededCount}, Failed={FailedCount}, DeadLettered={DeadLetteredCount}")]
public sealed record ProcessorDrainResult
{
    /// <summary>
    ///     Gets the number of processing passes executed during the drain, including the final idle pass.
    /// </summary>
    public int Passes { get; init; }

    /// <summary>
    ///     Gets the total number of envelopes leased across all passes.
    /// </summary>
    public int LeasedCount { get; init; }

    /// <summary>
    ///     Gets the total number of leased envelopes that completed successfully across all passes.
    /// </summary>
    public int SucceededCount { get; init; }

    /// <summary>
    ///     Gets the total number of leased envelopes that were marked failed for retry across all passes.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    ///     Gets the total number of leased envelopes that were moved to dead-letter state across all passes.
    /// </summary>
    public int DeadLetteredCount { get; init; }

    /// <summary>
    ///     Gets the wall-clock duration of the drain, including leasing and state updates.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }
}
