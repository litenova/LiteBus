using System;
using OpenTelemetry.Metrics;

namespace LiteBus.Transport.Extensions.OpenTelemetry;

/// <summary>
///     Registers LiteBus transport circuit breaker meters with OpenTelemetry providers.
/// </summary>
public static class LiteBusTransportOpenTelemetryExtensions
{
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
