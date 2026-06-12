namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     The aggregate status derived from one or more diagnostic probe outcomes.
/// </summary>
public enum DiagnosticAggregateStatus
{
    /// <summary>
    ///     All probes reported healthy status.
    /// </summary>
    Healthy,

    /// <summary>
    ///     At least one probe reported degraded status and none reported unhealthy status.
    /// </summary>
    Degraded,

    /// <summary>
    ///     At least one probe reported unhealthy status.
    /// </summary>
    Unhealthy
}
