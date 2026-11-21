using System;
using OpenTelemetry.Metrics;

namespace LiteBus.Auditing.Extensions.OpenTelemetry;

/// <summary>
///     Registers LiteBus auditing meters with OpenTelemetry providers.
/// </summary>
public static class LiteBusAuditingOpenTelemetryExtensions
{
    /// <summary>
    ///     Adds the LiteBus auditing meter to the meter provider builder.
    /// </summary>
    /// <param name="builder">The meter provider builder receiving LiteBus auditing metrics.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    public static MeterProviderBuilder AddLiteBusAuditingMetrics(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddMeter(LiteBusAuditingTelemetry.MeterName);
    }
}
