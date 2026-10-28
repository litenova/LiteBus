using System;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LiteBus.Messaging.Extensions.OpenTelemetry;

/// <summary>
///     Registers the LiteBus mediation activity source and meter with OpenTelemetry providers.
/// </summary>
/// <remarks>
///     The mediation instruments exist whether or not anything listens, and record nothing until something does.
///     These two calls are what makes them reach an exporter, the same way the inbox, outbox and transport adapters
///     work.
/// </remarks>
public static class LiteBusMediationOpenTelemetryExtensions
{
    /// <summary>
    ///     Adds the LiteBus mediation activity source to the tracer provider builder.
    /// </summary>
    /// <param name="builder">The tracer provider builder receiving LiteBus mediation instrumentation.</param>
    /// <returns>The tracer provider builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     One span per mediation by default. Per-stage child spans are opt-in through
    ///     <c>MessageModuleBuilder.UseTelemetry</c>, because mediation volume is orders of magnitude above durable
    ///     processing volume.
    /// </remarks>
    public static TracerProviderBuilder AddLiteBusMediationInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(LiteBusMediationTelemetry.ActivitySourceName);
    }

    /// <summary>
    ///     Adds the LiteBus mediation meter to the meter provider builder.
    /// </summary>
    /// <param name="builder">The meter provider builder receiving LiteBus mediation metrics.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Brings the duration histogram, the outcome counter, and the decision counter. The decision counter is the
    ///     one that answers which stage stopped a message, which was previously readable only from a stack trace.
    /// </remarks>
    public static MeterProviderBuilder AddLiteBusMediationMetrics(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddMeter(LiteBusMediationTelemetry.MeterName);
    }
}
