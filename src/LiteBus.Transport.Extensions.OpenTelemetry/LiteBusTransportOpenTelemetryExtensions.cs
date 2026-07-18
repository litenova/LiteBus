using System;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LiteBus.Transport.Extensions.OpenTelemetry;

/// <summary>
///     Registers LiteBus transport circuit breaker meters with OpenTelemetry providers.
/// </summary>
public static class LiteBusTransportOpenTelemetryExtensions
{
    /// <summary>
    ///     Adds the LiteBus transport activity source to the tracer provider builder.
    /// </summary>
    /// <param name="builder">The tracer provider builder receiving LiteBus transport instrumentation.</param>
    /// <returns>The tracer provider builder for method chaining.</returns>
    public static TracerProviderBuilder AddLiteBusTransportInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddSource(LiteBusTransportTelemetry.ActivitySourceName);
    }

    /// <summary>
    ///     Adds the LiteBus transport circuit breaker meter to the meter provider builder.
    /// </summary>
    /// <param name="builder">The meter provider builder receiving LiteBus transport metrics.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    public static MeterProviderBuilder AddLiteBusTransportMetrics(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddMeter(LiteBusTransportTelemetry.MeterName);
    }
}
