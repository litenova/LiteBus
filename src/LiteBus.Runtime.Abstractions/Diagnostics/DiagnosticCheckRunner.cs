using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Hosting;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Aggregates diagnostic probe outcomes for health and management surfaces.
/// </summary>
public static class DiagnosticCheckRunner
{
    /// <summary>
    ///     Runs manifest diagnostic probes and aggregates their status.
    /// </summary>
    /// <param name="manifest">The host manifest listing diagnostic probe descriptors.</param>
    /// <param name="services">The service provider used to resolve probe implementations.</param>
    /// <param name="failHealthWhenNoProbes">
    ///     When <see langword="true" /> and no probes are registered, the run reports
    ///     <see cref="DiagnosticAggregateStatus.Degraded" />.
    /// </param>
    /// <param name="cancellationToken">A token that cancels probe execution.</param>
    /// <returns>The aggregated probe outcomes.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="manifest" /> or <paramref name="services" /> is <see langword="null" />.
    /// </exception>
    public static async Task<DiagnosticCheckRunResult> RunAsync(
        LiteBusHostManifest manifest,
        IServiceProvider services,
        bool failHealthWhenNoProbes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(services);

        if (manifest.DiagnosticChecks.Count == 0)
        {
            if (failHealthWhenNoProbes)
            {
                return new DiagnosticCheckRunResult(
                    DiagnosticAggregateStatus.Degraded,
                    [
                        new DiagnosticProbeOutcome(
                            "litebus.probes",
                            DiagnosticStatus.Degraded,
                            "No diagnostic probes are registered.",
                            null)
                    ]);
            }

            return new DiagnosticCheckRunResult(
                DiagnosticAggregateStatus.Healthy,
                []);
        }

        var probes = new List<DiagnosticProbeOutcome>(manifest.DiagnosticChecks.Count);

        foreach (var descriptor in manifest.DiagnosticChecks)
        {
            if (services.GetService(descriptor.ImplementationType) is not IDiagnosticCheck check)
            {
                throw new InvalidOperationException(
                    $"Diagnostic check '{descriptor.Name}' is not registered in the service provider.");
            }

            var result = await DiagnosticCheckExecution.CheckAsync(descriptor, check, cancellationToken)
                .ConfigureAwait(false);

            probes.Add(new DiagnosticProbeOutcome(
                descriptor.Name,
                result.Status,
                result.Description,
                result.Data));
        }

        return new DiagnosticCheckRunResult(AggregateStatus(probes), probes);
    }

    /// <summary>
    ///     Derives the aggregate status from individual probe outcomes.
    /// </summary>
    /// <param name="probes">The probe outcomes collected during the run.</param>
    /// <returns>The aggregate status applied by health and management adapters.</returns>
    private static DiagnosticAggregateStatus AggregateStatus(IReadOnlyList<DiagnosticProbeOutcome> probes)
    {
        if (probes.All(probe => probe.Status == DiagnosticStatus.Healthy))
        {
            return DiagnosticAggregateStatus.Healthy;
        }

        if (probes.Any(probe => probe.Status == DiagnosticStatus.Unhealthy))
        {
            return DiagnosticAggregateStatus.Unhealthy;
        }

        return DiagnosticAggregateStatus.Degraded;
    }
}
