namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Describes the settlement performed after a transport consumer handler invocation.
/// </summary>
public enum TransportConsumerInvocationOutcome
{
    /// <summary>
    ///     The handler completed without the invoker settling the delivery.
    /// </summary>
    Handled = 0,

    /// <summary>
    ///     The invoker returned the delivery to the broker after a handler failure.
    /// </summary>
    Requeued = 1
}
