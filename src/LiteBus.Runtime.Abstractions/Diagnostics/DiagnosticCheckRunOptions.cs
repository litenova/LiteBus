using System;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Controls timeout and concurrency limits for one diagnostic check run.
/// </summary>
public sealed record DiagnosticCheckRunOptions
{
    /// <summary>
    ///     Gets the maximum number of probes that may execute concurrently.
    /// </summary>
    public int MaxParallelism { get; init; } = 4;

    /// <summary>
    ///     Gets the maximum time allowed for one probe.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the maximum time allowed for the whole diagnostic run.
    /// </summary>
    /// <value>
    ///     The run budget. When <see langword="null" /> (the default), the budget is derived from the probe count,
    ///     <see cref="MaxParallelism" />, and <see cref="Timeout" />. Probes still running when the budget expires are
    ///     reported as unhealthy. A negative value is rejected when health or management endpoints are registered and
    ///     when a run starts.
    /// </value>
    public TimeSpan? RunTimeout { get; init; }
}
