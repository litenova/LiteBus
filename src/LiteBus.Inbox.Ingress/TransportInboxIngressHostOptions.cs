using System;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Defines how the transport inbox ingress background service runs.
/// </summary>
public sealed class TransportInboxIngressHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the hosted ingress consumer loop is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between consumer restart attempts after startup or connection failure.
    /// </summary>
    public TimeSpan RetryPollInterval { get; set; } = TimeSpan.FromSeconds(5);
}