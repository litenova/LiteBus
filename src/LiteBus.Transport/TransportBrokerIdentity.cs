namespace LiteBus.Transport;

/// <summary>
///     Identifies the active transport broker adapter for telemetry dimensions.
/// </summary>
public sealed class TransportBrokerIdentity
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportBrokerIdentity" /> class.
    /// </summary>
    /// <param name="broker">The stable broker name recorded on transport metrics.</param>
    public TransportBrokerIdentity(string broker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broker);
        Broker = broker;
    }

    /// <summary>
    ///     Gets the stable broker name recorded on transport metrics.
    /// </summary>
    /// <value>A lowercase broker identifier such as <c>amqp</c> or <c>kafka</c>.</value>
    public string Broker { get; }
}
