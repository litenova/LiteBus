using System;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Defines how the transport inbox ingress background service runs.
/// </summary>
public sealed class TransportInboxIngressHostOptions
{
    /// <summary>
    ///     Gets the minimum delay between consumer restart attempts when no retry interval is configured.
    /// </summary>
    /// <value>
    ///     The consumer waits at least this long between restarts so a zero
    ///     <see cref="RetryPollInterval" /> cannot turn a persistent failure into a hot restart loop.
    /// </value>
    public static TimeSpan MinimumRestartDelay { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets or sets a value indicating whether the hosted ingress consumer loop is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between consumer restart attempts after startup or connection failure.
    /// </summary>
    /// <value>
    ///     Zero keeps validation passing but still waits <see cref="MinimumRestartDelay" /> between restarts.
    /// </value>
    public TimeSpan RetryPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Validates ingress retry timing before the consumer loop starts.
    /// </summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(RetryPollInterval, TimeSpan.Zero);
    }
}
