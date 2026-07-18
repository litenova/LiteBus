using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Extensions.Diagnostics.HealthChecks;

/// <summary>
///     Configures manifest probe execution and empty-manifest readiness behavior for <see cref="LiteBusHealthCheck" />.
/// </summary>
public sealed class LiteBusHealthCheckOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether zero registered probes should fail the health check.
    /// </summary>
    /// <value>
    ///     When <see langword="true" />, the check reports degraded when the manifest has no probes. When
    ///     <see langword="false" />, the check reports healthy with no probe data. Defaults to <see langword="true" /> to
    ///     match <c>GET /litebus/health</c> when <c>LiteBusManagementOptions.FailHealthWhenNoProbes</c> is enabled.
    /// </value>
    public bool FailHealthWhenNoProbes { get; set; } = true;

    /// <summary>
    ///     Gets or sets the timeout and parallelism limits applied to manifest diagnostic probes.
    /// </summary>
    public DiagnosticCheckRunOptions DiagnosticChecks { get; set; } = new();
}
