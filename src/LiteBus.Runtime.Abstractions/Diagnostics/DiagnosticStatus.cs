namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Describes the outcome of a framework-neutral diagnostic probe.
/// </summary>
public enum DiagnosticStatus
{
    /// <summary>
    ///     The probe succeeded and reported no actionable issue.
    /// </summary>
    Healthy = 0,

    /// <summary>
    ///     The probe succeeded but reported a condition that may need attention.
    /// </summary>
    Degraded = 1,

    /// <summary>
    ///     The probe failed or reported a condition that requires intervention.
    /// </summary>
    Unhealthy = 2
}