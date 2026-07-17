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
        return await RunAsync(
                manifest,
                services,
                failHealthWhenNoProbes,
                new DiagnosticCheckRunOptions(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs manifest diagnostic probes concurrently with bounded timeout and failure isolation.
    /// </summary>
    /// <param name="manifest">The host manifest listing diagnostic probe descriptors.</param>
    /// <param name="services">The service provider used to resolve probe implementations.</param>
    /// <param name="failHealthWhenNoProbes">Whether an empty manifest reports degraded status.</param>
    /// <param name="options">The timeout and concurrency limits for this run.</param>
    /// <param name="cancellationToken">A token that cancels probe execution.</param>
    /// <returns>The aggregated probe outcomes.</returns>
    public static async Task<DiagnosticCheckRunResult> RunAsync(
        LiteBusHostManifest manifest,
        IServiceProvider services,
        bool failHealthWhenNoProbes,
        DiagnosticCheckRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxParallelism);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Timeout.Ticks);

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

        using var gate = new SemaphoreSlim(options.MaxParallelism);
        var tasks = manifest.DiagnosticChecks.Select(async descriptor =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            CancellationTokenSource? timeoutCts = null;

            try
            {
                if (services.GetService(descriptor.ImplementationType) is not IDiagnosticCheck check)
                {
                    return new DiagnosticProbeOutcome(
                        descriptor.Name,
                        DiagnosticStatus.Unhealthy,
                        "The diagnostic check is not registered.",
                        null);
                }

                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var result = await DiagnosticCheckExecution.CheckAsync(descriptor, check, timeoutCts.Token)
                    .WaitAsync(options.Timeout, cancellationToken)
                    .ConfigureAwait(false);

                return new DiagnosticProbeOutcome(descriptor.Name, result.Status, result.Description, result.Data);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                timeoutCts?.Cancel();
                return new DiagnosticProbeOutcome(
                    descriptor.Name,
                    DiagnosticStatus.Unhealthy,
                    "The diagnostic check timed out.",
                    null);
            }
#pragma warning disable CA1031 // A diagnostic probe must not prevent sibling probes from reporting.
            catch (Exception)
#pragma warning restore CA1031
            {
                return new DiagnosticProbeOutcome(
                    descriptor.Name,
                    DiagnosticStatus.Unhealthy,
                    "The diagnostic check failed.",
                    null);
            }
            finally
            {
                timeoutCts?.Dispose();
                gate.Release();
            }
        });

        var probes = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();

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
