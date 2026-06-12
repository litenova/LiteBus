using System;
using OpenTelemetry.Metrics;
namespace LiteBus.Transport.Amqp.Extensions.OpenTelemetry;

/// <summary>
///     Registers LiteBus AMQP transport circuit breaker meters with OpenTelemetry providers.
/// </summary>
public static class LiteBusTransportAmqpOpenTelemetryExtensions
{
    /// <summary>
    ///     Adds the shared LiteBus transport meter to the meter provider builder.
    /// </summary>
    /// <param name="builder">The meter provider builder receiving LiteBus transport metrics.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    /// <remarks>
    ///     AMQP circuit breaker metrics are recorded on <see cref="LiteBusTransportTelemetry.MeterName" /> with the
    ///     <see cref="LiteBusTransportTelemetry.BrokerTagName" /> dimension set to <c>amqp</c>.
    /// </remarks>
    public static MeterProviderBuilder AddLiteBusAmqpMetrics(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddMeter(LiteBusTransportTelemetry.MeterName);
    }
}
