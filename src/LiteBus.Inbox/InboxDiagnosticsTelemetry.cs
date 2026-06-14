using System.Diagnostics.Metrics;

namespace LiteBus.Inbox;

/// <summary>
///     OpenTelemetry counters for inbox diagnostics probes.
/// </summary>
internal static class InboxDiagnosticsTelemetry
{
    /// <summary>
    ///     Gets the meter used for inbox diagnostics counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusInboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented when queue depth probes fail against the backing store.
    /// </summary>
    private static readonly Counter<long> UnavailableCounter =
        Meter.CreateCounter<long>(LiteBusInboxTelemetry.DiagnosticsUnavailableInstrumentName);

    /// <summary>
    ///     Records that an inbox diagnostics probe could not read queue depth from the store.
    /// </summary>
    public static void RecordUnavailable()
    {
        UnavailableCounter.Add(1);
    }
}
