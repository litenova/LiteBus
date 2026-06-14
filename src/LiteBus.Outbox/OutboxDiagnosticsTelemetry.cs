using System.Diagnostics.Metrics;

namespace LiteBus.Outbox;

/// <summary>
///     OpenTelemetry counters for outbox diagnostics probes.
/// </summary>
internal static class OutboxDiagnosticsTelemetry
{
    /// <summary>
    ///     Gets the meter used for outbox diagnostics counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusOutboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented when queue depth probes fail against the backing store.
    /// </summary>
    private static readonly Counter<long> UnavailableCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.DiagnosticsUnavailableInstrumentName);

    /// <summary>
    ///     Records that an outbox diagnostics probe could not read queue depth from the store.
    /// </summary>
    public static void RecordUnavailable()
    {
        UnavailableCounter.Add(1);
    }
}
