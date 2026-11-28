using System.Collections.Generic;
using System.Diagnostics.Metrics;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Recording;

/// <summary>
///     Records the auditing module's instruments on the <see cref="LiteBusAuditingTelemetry.MeterName" /> meter.
/// </summary>
internal static class AuditingMetrics
{
    /// <summary>
    ///     The process-wide meter the auditing instruments belong to.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusAuditingTelemetry.MeterName);

    /// <summary>
    ///     The counter of audit records that could not be written.
    /// </summary>
    private static readonly Counter<long> WriteFailures = Meter.CreateCounter<long>(
        LiteBusAuditingTelemetry.WriteFailuresInstrumentName,
        unit: "{record}",
        description: "Audit records that could not be written.");

    /// <summary>
    ///     Counts one audit record that could not be written.
    /// </summary>
    /// <param name="record">The record that was not written.</param>
    internal static void RecordWriteFailure(AuditRecord record)
    {
        WriteFailures.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusAuditingTelemetry.OutcomeAttributeName, record.Outcome.ToString()),
            new KeyValuePair<string, object?>(LiteBusAuditingTelemetry.ActionAttributeName, record.Action));
    }
}
