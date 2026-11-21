namespace LiteBus.Auditing;

/// <summary>
///     Names the meter, instruments and attributes the auditing module records, so an exporter can subscribe to
///     them by constant.
/// </summary>
/// <remarks>
///     These names are part of the consumer contract: renaming or removing one is a breaking change. Register the
///     meter with <c>AddLiteBusAuditingMetrics()</c> from <c>LiteBus.Auditing.Extensions.OpenTelemetry</c>, or with
///     <c>AddMeter(LiteBusAuditingTelemetry.MeterName)</c> on any meter provider.
/// </remarks>
public static class LiteBusAuditingTelemetry
{
    /// <summary>
    ///     The name of the meter the auditing module records on.
    /// </summary>
    public const string MeterName = "LiteBus.Auditing";

    /// <summary>
    ///     The counter of audit records that could not be written, one per failed record.
    /// </summary>
    public const string WriteFailuresInstrumentName = "litebus.audit.write.failures";

    /// <summary>
    ///     The attribute naming the audit outcome of the record that could not be written, such as <c>Denied</c>.
    /// </summary>
    public const string OutcomeAttributeName = "litebus.audit.outcome";

    /// <summary>
    ///     The attribute naming the audited action of the record that could not be written.
    /// </summary>
    public const string ActionAttributeName = "litebus.audit.action";
}
