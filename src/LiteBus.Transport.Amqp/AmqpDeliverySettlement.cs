using System.Threading;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Tracks broker settlement for one AMQP delivery so a handler settle and the failure-path requeue cannot both
///     reach the broker.
/// </summary>
/// <remarks>
///     RabbitMQ closes the channel when a client acknowledges an unknown delivery tag, so the second settle of one
///     delivery is skipped rather than sent.
/// </remarks>
internal sealed class AmqpDeliverySettlement
{
    /// <summary>
    ///     Indicates that the delivery has not been settled.
    /// </summary>
    private const int Pending = 0;

    /// <summary>
    ///     Indicates that the delivery has been settled.
    /// </summary>
    private const int Settled = 1;

    /// <summary>
    ///     Tracks whether settlement has already started.
    /// </summary>
    private int _state;

    /// <summary>
    ///     Gets a value indicating whether the delivery has been settled.
    /// </summary>
    internal bool IsSettled => Volatile.Read(ref _state) == Settled;

    /// <summary>
    ///     Marks the delivery as settled when no settle has started yet.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when this call settled the delivery; <see langword="false" /> when the delivery
    ///     was already settled.
    /// </returns>
    internal bool TryMarkSettled()
    {
        return Interlocked.CompareExchange(ref _state, Settled, Pending) == Pending;
    }
}
