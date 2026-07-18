using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LiteBus.Transport;

/// <summary>
///     OpenTelemetry activity and metric instrumentation for the transport circuit breaker.
/// </summary>
internal static class TransportCircuitBreakerTelemetry
{
    /// <summary>
    ///     Gets the meter used for circuit breaker counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusTransportTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented when a failure is recorded while the circuit is closed.
    /// </summary>
    private static readonly Counter<long> FailureRecordedCounter =
        Meter.CreateCounter<long>("litebus.transport.circuit_breaker.failure_recorded");

    /// <summary>
    ///     Gets the counter incremented when the circuit opens after reaching the failure threshold.
    /// </summary>
    private static readonly Counter<long> OpenedCounter =
        Meter.CreateCounter<long>("litebus.transport.circuit_breaker.opened");

    /// <summary>
    ///     Records that a failure was observed while the circuit remains closed.
    /// </summary>
    /// <param name="failureCount">The consecutive failure count after the increment.</param>
    public static void RecordFailureObserved(int failureCount)
    {
        FailureRecordedCounter.Add(1);

        Activity.Current?.AddEvent(new ActivityEvent(
            "litebus.transport.circuit_breaker.failure_recorded",
            tags: new ActivityTagsCollection { ["litebus.transport.circuit_breaker.failure_count"] = failureCount }));
    }

    /// <summary>
    ///     Records that the circuit opened after reaching the configured failure threshold.
    /// </summary>
    public static void RecordCircuitOpened()
    {
        OpenedCounter.Add(1);
        Activity.Current?.AddEvent(new ActivityEvent("litebus.transport.circuit_breaker.opened"));
    }
}