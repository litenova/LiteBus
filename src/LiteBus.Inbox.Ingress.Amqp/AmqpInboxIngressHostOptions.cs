namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Defines how the AMQP inbox ingress background service runs.
/// </summary>
public sealed class AmqpInboxIngressHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the hosted ingress consumer loop is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
