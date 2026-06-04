using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Controls the outbox processor background service lifecycle and polling.
/// </summary>
public sealed class OutboxProcessorHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the processor is active.
    /// </summary>
    /// <value>The default is <see langword="true" />.</value>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between poll cycles when the previous batch was empty or partial.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets or sets the delay after host start before the first poll cycle.
    ///     Allows dependent services to stabilise.
    /// </summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Gets or sets a value indicating whether the poll delay is skipped after a full batch,
    ///     reducing latency under sustained load.
    /// </summary>
    public bool UseAdaptivePolling { get; set; } = true;
}
