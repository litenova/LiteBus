using System;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Public OpenTelemetry instrument names for AMQP transport telemetry.
/// </summary>
/// <remarks>
///     Circuit breaker metrics are recorded on the shared <see cref="Transport.LiteBusTransportTelemetry" /> meter with
///     the <see cref="Transport.LiteBusTransportTelemetry.BrokerTagName" /> dimension set to <c>amqp</c>. These names are
///     retained for backward-compatible registration only.
/// </remarks>
[Obsolete("Use LiteBusTransportTelemetry on the LiteBus.Transport meter with the litebus.transport.broker tag.")]
public static class LiteBusTransportAmqpTelemetry
{
    /// <summary>
    ///     Gets the meter name used for AMQP transport metrics.
    /// </summary>
    [Obsolete("Use LiteBusTransportTelemetry.MeterName with the litebus.transport.broker tag.")]
    public const string MeterName = "LiteBus.Transport.Amqp";

    /// <summary>
    ///     Gets the instrument name indicating whether the AMQP circuit breaker is open.
    /// </summary>
    [Obsolete("Use LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName with the litebus.transport.broker tag.")]
    public const string CircuitBreakerOpenInstrumentName = "litebus.amqp.circuit_breaker.open";

    /// <summary>
    ///     Gets the instrument name for the current AMQP circuit breaker failure count.
    /// </summary>
    [Obsolete("Use LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName with the litebus.transport.broker tag.")]
    public const string CircuitBreakerFailureCountInstrumentName = "litebus.amqp.circuit_breaker.failure_count";
}
