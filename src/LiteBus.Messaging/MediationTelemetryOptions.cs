namespace LiteBus.Messaging;

/// <summary>
///     What mediation records, and at what cost.
/// </summary>
/// <remarks>
///     <para>
///         Mediation is the hot path. An <c>ActivitySource</c> with no listener is close to free and the counters are
///         cheap, so both are on by default. A child span per pre stage on every message is not, and mediation volume
///         is orders of magnitude above inbox volume, so that one is opt-in.
///     </para>
///     <para>
///         Configure through <c>MessageModuleBuilder.UseTelemetry</c>. Turning everything off leaves the instruments
///         defined and unused, which costs one branch per mediation.
///     </para>
/// </remarks>
public sealed record MediationTelemetryOptions
{
    /// <summary>
    ///     Gets a value indicating whether one span is started per mediation.
    /// </summary>
    /// <value><see langword="true" /> by default. A source with no listener does almost no work.</value>
    public bool Spans { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether a child span is started per pre stage.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default. Turn it on while investigating where mediation time goes, and measure
    ///     before leaving it on for a high-volume service.
    /// </value>
    public bool StageSpans { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the duration histogram and outcome counter are recorded.
    /// </summary>
    /// <value><see langword="true" /> by default.</value>
    public bool Metrics { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether per-stage durations are recorded.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default, for the same reason <see cref="StageSpans" /> is: one measurement per
    ///     stage per message is four to five times the instrument traffic of one per message.
    /// </value>
    public bool StageMetrics { get; init; }

    /// <summary>
    ///     Gets the options with nothing recorded.
    /// </summary>
    /// <value>
    ///     Useful for a benchmark, and for a host that exports mediation telemetry through its own instrumentation and
    ///     does not want two sources of the same measurement.
    /// </value>
    public static MediationTelemetryOptions Disabled { get; } = new()
    {
        Spans = false,
        StageSpans = false,
        Metrics = false,
        StageMetrics = false
    };
}
