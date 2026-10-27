namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Configures enqueue-time outbox behaviors applied by the envelope factory and the collected-events staging handler.
/// </summary>
/// <remarks>
///     <para>
///         The outbox module builds one instance from its builder flags and registers it as a singleton, so the values
///         are fixed at composition time. Direct factory constructions that omit the options behave as if both flags
///         were <see langword="false" />.
///     </para>
/// </remarks>
public sealed record OutboxEnqueueOptions
{
    /// <summary>
    ///     Gets a value indicating whether envelopes stamp trace metadata from the ambient execution context when the enqueue item carries none.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default. When <see langword="true" />, an item whose trace is
    ///     <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.None" /> is stored with
    ///     <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.MessageTrace.FromAmbient" /> instead. An explicit
    ///     item trace always wins over the ambient value.
    /// </value>
    public bool UseAmbientTrace { get; init; }

    /// <summary>
    ///     Gets a value indicating whether one enqueue writes one envelope per subscribed main handler of the event.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default. When <see langword="true" />, each envelope records the full name of the
    ///     handler it is addressed to, and in-process dispatch publishes each envelope to that handler alone, so every
    ///     reaction gets its own retry, dead letter, and replay.
    /// </value>
    public bool PerHandlerDelivery { get; init; }
}
