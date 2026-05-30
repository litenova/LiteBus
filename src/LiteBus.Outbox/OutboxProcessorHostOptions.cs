using System;

namespace LiteBus.Outbox;

/// <summary>
///     Defines how the outbox processor background work runs repeated publication passes.
/// </summary>
/// <remarks>
///     <para>
///         These options control the background loop only. Batch size, lease duration, and retry behavior remain on
///         <see cref="Abstractions.OutboxProcessorOptions" /> because they describe how each pass leases and publishes messages.
///     </para>
/// </remarks>
public sealed class OutboxProcessorHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the processor background loop is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between completed processing passes when adaptive polling does not skip the delay.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets or sets the delay applied once before the first processing pass starts.
    /// </summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Gets or sets a value indicating whether the loop should poll again immediately when the previous pass
    ///     leased a full batch, which usually means more due messages may still be waiting.
    /// </summary>
    public bool UseAdaptivePolling { get; set; } = true;
}
