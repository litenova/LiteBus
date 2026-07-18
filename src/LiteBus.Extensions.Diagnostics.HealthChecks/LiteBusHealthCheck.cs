using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Extensions.Diagnostics.HealthChecks;

/// <summary>
///     Runs registered <see cref="IDiagnosticCheck" /> probes from <see cref="LiteBusHostManifest" />.
/// </summary>
public sealed class LiteBusHealthCheck : IHealthCheck
{
    /// <summary>
    ///     The host manifest that lists diagnostic probe descriptors.
    /// </summary>
    private readonly LiteBusHostManifest _manifest;

    /// <summary>
    ///     The options controlling probe execution and zero-probe policy.
    /// </summary>
    private readonly LiteBusHealthCheckOptions _options;

    /// <summary>
    ///     The service provider used to resolve probe implementations.
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHealthCheck" /> class.
    /// </summary>
    /// <param name="manifest">The host manifest that lists diagnostic probe descriptors.</param>
    /// <param name="services">The service provider used to resolve probe implementations.</param>
    /// <param name="options">The options controlling probe execution and zero-probe policy.</param>
    public LiteBusHealthCheck(
        LiteBusHostManifest manifest,
        IServiceProvider services,
        LiteBusHealthCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        _manifest = manifest;
        _services = services;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var runResult = await DiagnosticCheckRunner.RunAsync(
                _manifest,
                _services,
                _options.FailHealthWhenNoProbes,
                _options.DiagnosticChecks,
                cancellationToken)
            .ConfigureAwait(false);

        return MapHealthCheckResult(runResult);
    }

    /// <summary>
    ///     Maps a shared diagnostic run result to ASP.NET Core health check status.
    /// </summary>
    /// <param name="runResult">The aggregated diagnostic run result.</param>
    /// <returns>The health check result reported to the host.</returns>
    private static HealthCheckResult MapHealthCheckResult(DiagnosticCheckRunResult runResult)
    {
        var data = BuildData(runResult.Probes);

        return runResult.Status switch
        {
            DiagnosticAggregateStatus.Healthy when runResult.Probes.Count == 0 =>
                HealthCheckResult.Healthy("No LiteBus diagnostic probes are registered."),
            DiagnosticAggregateStatus.Healthy =>
                HealthCheckResult.Healthy("All LiteBus diagnostic probes succeeded.", data),
            DiagnosticAggregateStatus.Unhealthy =>
                HealthCheckResult.Unhealthy(
                    "One or more LiteBus diagnostic probes reported unhealthy status.",
                    data: data),
            _ => HealthCheckResult.Degraded(
                runResult.Probes is [{ Name: "litebus.probes" }]
                    ? "No LiteBus diagnostic probes are registered."
                    : "One or more LiteBus diagnostic probes reported degraded status.",
                data: data)
        };
    }

    /// <summary>
    ///     Flattens probe outcomes into health check report data.
    /// </summary>
    /// <param name="probes">The probe outcomes collected during the check.</param>
    /// <returns>Structured values attached to the health check result.</returns>
    private static Dictionary<string, object> BuildData(IReadOnlyList<DiagnosticProbeOutcome> probes)
    {
        return new Dictionary<string, object>
        {
            ["probes"] = probes.Select(probe => new Dictionary<string, object?>
            {
                ["status"] = probe.Status.ToString(),
                ["description"] = probe.Description,
                ["data"] = probe.Data
            }).ToArray()
        };
    }
}
