using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
    ///     The options controlling zero-probe policy.
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
    /// <param name="options">The options controlling zero-probe policy.</param>
    public LiteBusHealthCheck(
        LiteBusHostManifest manifest,
        IServiceProvider services,
        LiteBusHealthCheckOptions options)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_manifest.DiagnosticChecks.Count == 0)
        {
            if (_options.FailHealthWhenNoProbes)
            {
                return HealthCheckResult.Degraded("No LiteBus diagnostic probes are registered.");
            }

            return HealthCheckResult.Healthy("No LiteBus diagnostic probes are registered.");
        }

        var results = new List<DiagnosticResult>();

        foreach (var descriptor in _manifest.DiagnosticChecks)
        {
            var check = (IDiagnosticCheck) _services.GetRequiredService(descriptor.ImplementationType);
            results.Add(await DiagnosticCheckExecution.CheckAsync(descriptor, check, cancellationToken).ConfigureAwait(false));
        }

        if (results.All(result => result.Status == DiagnosticStatus.Healthy))
        {
            return HealthCheckResult.Healthy("All LiteBus diagnostic probes succeeded.", BuildData(results));
        }

        if (results.Any(result => result.Status == DiagnosticStatus.Unhealthy))
        {
            return HealthCheckResult.Unhealthy(
                "One or more LiteBus diagnostic probes reported unhealthy status.",
                data: BuildData(results));
        }

        return HealthCheckResult.Degraded(
            "One or more LiteBus diagnostic probes reported degraded status.",
            data: BuildData(results));
    }

    /// <summary>
    ///     Flattens probe outcomes into health check report data.
    /// </summary>
    /// <param name="results">The probe outcomes collected during the check.</param>
    /// <returns>Structured values attached to the health check result.</returns>
    private static IReadOnlyDictionary<string, object> BuildData(IReadOnlyList<DiagnosticResult> results)
    {
        return new Dictionary<string, object>
        {
            ["probes"] = results.Select(result => new Dictionary<string, object?>
            {
                ["status"] = result.Status.ToString(),
                ["description"] = result.Description,
                ["data"] = result.Data
            }).ToArray()
        };
    }
}
