using System;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     Defines how the audit link sweep background loop runs.
/// </summary>
/// <remarks>
///     The sweep links the staged records a crash left behind: the <see cref="AuditLink.AfterCommit" />
///     path links inline right after commit, and this loop covers what the crash interrupted. The defaults
///     link records staged over a minute ago every minute, well before verification reports a record left
///     unlinked past its own threshold, while leaving records an inline linker just staged for that linker
///     instead of contending with it.
/// </remarks>
public sealed class AuditLinkSweepHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the sweep loop runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between sweep passes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Gets or sets how long a staged record waits before the sweep links it.
    /// </summary>
    public TimeSpan OlderThan { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Validates the sweep timing values before the background loop starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="Interval" /> is zero or negative, or <see cref="OlderThan" /> is negative.
    /// </exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(OlderThan, TimeSpan.Zero);
    }
}
