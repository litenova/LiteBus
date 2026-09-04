namespace LiteBus.Messaging;

/// <summary>
///     Public OpenTelemetry instrument and attribute names for in-process mediation.
/// </summary>
/// <remarks>
///     <para>
///         Mediation was the one path LiteBus could not see. The inbox, the outbox, the transport and each broker
///         adapter all publish instruments, and the library's primary job, dispatching a command, a query or an event
///         to its handlers, published none. "Which stage denied this" was answerable only by reading a stack trace.
///     </para>
///     <para>
///         These names are part of the consumer contract, like every other telemetry name in LiteBus. Treat a rename
///         or a removal as a breaking change, because a dashboard and an alert rule are built on them.
///     </para>
///     <para>
///         Register the source and the meter through <c>LiteBus.Messaging.Extensions.OpenTelemetry</c>. Metrics are
///         recorded by default; the per-stage spans are opt-in through <see cref="MediationTelemetryOptions" />,
///         because mediation volume is orders of magnitude above durable-processing volume and a child span per stage
///         on every message is not free.
///     </para>
/// </remarks>
public static class LiteBusMediationTelemetry
{
    /// <summary>
    ///     The activity source name used for mediation spans.
    /// </summary>
    public const string ActivitySourceName = "LiteBus.Mediation";

    /// <summary>
    ///     The meter name used for mediation metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Mediation";

    /// <summary>
    ///     The histogram instrument name for total mediation duration in milliseconds.
    /// </summary>
    public const string DurationInstrumentName = "litebus.mediation.duration";

    /// <summary>
    ///     The counter instrument name incremented once per completed mediation.
    /// </summary>
    /// <remarks>
    ///     Tagged with the outcome and, for a stopped mediation, the decision's code. That is what answers "what is
    ///     being denied, and why" without a single log line, which is the question the audit and authorization
    ///     features exist to serve.
    /// </remarks>
    public const string CountInstrumentName = "litebus.mediation.count";

    /// <summary>
    ///     The histogram instrument name for one pre-stage's duration in milliseconds.
    /// </summary>
    public const string StageDurationInstrumentName = "litebus.mediation.stage.duration";

    /// <summary>
    ///     The counter instrument name incremented when a stage stops a mediation.
    /// </summary>
    /// <remarks>
    ///     Tagged with the stage and the handler that decided, which turns "which stage denied this" from a stack
    ///     trace into a filter.
    /// </remarks>
    public const string DecisionsInstrumentName = "litebus.mediation.decisions";

    /// <summary>
    ///     The attribute key carrying the mediated message type name.
    /// </summary>
    public const string MessageAttributeName = "litebus.message";

    /// <summary>
    ///     The attribute key carrying how the mediation ended.
    /// </summary>
    public const string OutcomeAttributeName = "litebus.outcome";

    /// <summary>
    ///     The attribute key carrying the machine-readable code a decision supplied.
    /// </summary>
    public const string CodeAttributeName = "litebus.code";

    /// <summary>
    ///     The attribute key carrying the pre stage a measurement or decision belongs to.
    /// </summary>
    public const string StageAttributeName = "litebus.stage";

    /// <summary>
    ///     The attribute key carrying the handler type that stopped the mediation.
    /// </summary>
    public const string DecidedByAttributeName = "litebus.decided_by";
}
