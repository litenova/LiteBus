using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines how the outbox retention cleanup background loop runs.
/// </summary>
public sealed class OutboxCleanupHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether retention cleanup is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between cleanup passes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Gets or sets how long completed outbox messages are retained before deletion.
    /// </summary>
    /// <value>
    ///     When <see langword="null" />, the cleanup loop does not delete rows.
    /// </value>
    public TimeSpan? Retention { get; set; }
}