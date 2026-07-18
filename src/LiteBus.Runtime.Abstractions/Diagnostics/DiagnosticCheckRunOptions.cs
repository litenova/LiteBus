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
}
