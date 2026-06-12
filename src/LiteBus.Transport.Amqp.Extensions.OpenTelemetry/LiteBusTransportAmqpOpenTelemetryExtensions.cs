using System;
using LiteBus.Transport.Amqp;
using OpenTelemetry.Metrics;

namespace LiteBus.Transport.Amqp.Extensions.OpenTelemetry;

/// <summary>
///     Registers LiteBus AMQP transport circuit breaker meters with OpenTelemetry providers.
/// </summary>
public static class LiteBusTransportAmqpOpenTelemetryExtensions
{
    /// <summary>
    ///     Adds the LiteBus AMQP transport meter to the meter provider builder.
    /// </summary>
    /// <param name="builder">The meter provider builder receiving LiteBus AMQP metrics.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    public static MeterProviderBuilder AddLiteBusAmqpMetrics(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddMeter(LiteBusTransportAmqpTelemetry.MeterName);
    }
}
