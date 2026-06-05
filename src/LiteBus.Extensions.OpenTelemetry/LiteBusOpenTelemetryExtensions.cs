using System;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Transport.Amqp;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LiteBus.Extensions.OpenTelemetry;

/// <summary>
///     Registers LiteBus activity sources and meters with OpenTelemetry providers.
/// </summary>
public static class LiteBusOpenTelemetryExtensions
{
    /// <summary>
    ///     Adds LiteBus inbox and outbox activity sources to the tracer provider builder.
    /// </summary>
    /// <param name="builder">The tracer provider builder receiving LiteBus instrumentation.</param>
    /// <returns>The tracer provider builder for method chaining.</returns>
    public static TracerProviderBuilder AddLiteBusInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddSource(LiteBusInboxTelemetry.ActivitySourceName)
            .AddSource(LiteBusOutboxTelemetry.ActivitySourceName);
    }

    /// <summary>
    ///     Adds LiteBus inbox, outbox, and AMQP meters to the meter provider builder.
    /// </summary>
    /// <param name="builder">The meter provider builder receiving LiteBus metrics.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    public static MeterProviderBuilder AddLiteBusMetrics(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddMeter(LiteBusInboxTelemetry.MeterName)
            .AddMeter(LiteBusOutboxTelemetry.MeterName)
            .AddMeter(LiteBusAmqpTelemetry.MeterName);
    }
}
