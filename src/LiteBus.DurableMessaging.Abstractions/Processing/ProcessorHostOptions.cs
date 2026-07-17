using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Controls shared durable processor background service lifecycle and polling behavior.
/// </summary>
public class ProcessorHostOptions
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
    /// </summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Gets or sets a value indicating whether the poll delay is skipped after a full batch.
    /// </summary>
    public bool UseAdaptivePolling { get; set; } = true;
}
