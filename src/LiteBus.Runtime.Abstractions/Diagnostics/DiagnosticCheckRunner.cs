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
    ///     The grace applied after a computed run budget expires before unfinished probes are reported as over budget.
    /// </summary>
    private static readonly TimeSpan RunBudgetGracePeriod = TimeSpan.FromSeconds(1);

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
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="manifest" />, <paramref name="services" />, or <paramref name="options" /> is
    ///     <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="DiagnosticCheckRunOptions.MaxParallelism" /> or
    ///     <see cref="DiagnosticCheckRunOptions.Timeout" /> is not positive, or when
    ///     <see cref="DiagnosticCheckRunOptions.RunTimeout" /> is negative.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken" /> is cancelled.
    /// </exception>
    /// <remarks>
    ///     The run budget is <see cref="DiagnosticCheckRunOptions.RunTimeout" /> when set, and otherwise the worst-case
    ///     packing of the probes into <see cref="DiagnosticCheckRunOptions.MaxParallelism" /> lanes at
    ///     <see cref="DiagnosticCheckRunOptions.Timeout" /> per probe. Probes still running when an explicit budget
    ///     expires are cancelled and reported as unhealthy; after a computed budget expires the runner first allows
    ///     in-flight per-probe timeouts to land, since the computed budget already equals their worst case.
    /// </remarks>
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

        if (options.RunTimeout is { } configuredRunTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(configuredRunTimeout.Ticks);
        }

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

        var runTimeout = options.RunTimeout ?? ComputeDefaultRunTimeout(manifest.DiagnosticChecks.Count, options);

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runToken = runCts.Token;

        var gate = new SemaphoreSlim(options.MaxParallelism);
        var tasks = manifest.DiagnosticChecks.Select(async descriptor =>
        {
            await gate.WaitAsync(runToken).ConfigureAwait(false);

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

                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
                var result = await DiagnosticCheckExecution.CheckAsync(descriptor, check, timeoutCts.Token)
                    .WaitAsync(options.Timeout, runToken)
                    .ConfigureAwait(false);

                return new DiagnosticProbeOutcome(descriptor.Name, result.Status, result.Description, result.Data);
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                // WaitAsync observes the run token before the linked probe source does; cancel the probe explicitly
                // so the finally block below cannot dispose its source before the cancellation reaches the probe.
                timeoutCts?.Cancel();
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
            catch (DiagnosticCheckNameMismatchException exception)
            {
                return new DiagnosticProbeOutcome(
                    descriptor.Name,
                    DiagnosticStatus.Unhealthy,
                    exception.Message,
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
        }).ToArray();

        try
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(runTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (options.RunTimeout is null)
                {
                    try
                    {
                        await Task.WhenAll(tasks).WaitAsync(RunBudgetGracePeriod, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // Stragglers remain; cancel and report them below.
                    }
                }

                runCts.Cancel();
            }

            var probes = new List<DiagnosticProbeOutcome>(tasks.Length);

            for (var i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].IsCompletedSuccessfully)
                {
                    probes.Add(await tasks[i].ConfigureAwait(false));
                }
                else
                {
                    probes.Add(new DiagnosticProbeOutcome(
                        manifest.DiagnosticChecks[i].Name,
                        DiagnosticStatus.Unhealthy,
                        "The diagnostic run exceeded its budget.",
                        null));
                }
            }

            return new DiagnosticCheckRunResult(AggregateStatus(probes), probes);
        }
        finally
        {
            DisposeGateWhenProbesComplete(gate, tasks);
        }
    }

    /// <summary>
    ///     Computes the default run budget from the probe count, parallelism, and per-probe timeout.
    /// </summary>
    /// <param name="probeCount">The number of probes in the run.</param>
    /// <param name="options">The timeout and concurrency limits for this run.</param>
    /// <returns>The worst-case wall time for the run when no explicit budget is configured.</returns>
    private static TimeSpan ComputeDefaultRunTimeout(int probeCount, DiagnosticCheckRunOptions options)
    {
        var waves = (probeCount + options.MaxParallelism - 1) / options.MaxParallelism;

        if (options.Timeout.Ticks > TimeSpan.MaxValue.Ticks / waves)
        {
            return Timeout.InfiniteTimeSpan;
        }

        return TimeSpan.FromTicks(waves * options.Timeout.Ticks);
    }

    /// <summary>
    ///     Disposes the parallelism gate once every probe task has finished.
    /// </summary>
    /// <param name="gate">The parallelism gate shared by the probe tasks.</param>
    /// <param name="tasks">The probe tasks of the finished run.</param>
    /// <remarks>
    ///     The run may return while probes are still winding down after a budget timeout. Disposing the gate eagerly
    ///     would fault their release path, so a run whose probes all finished disposes it inline and any other run
    ///     defers disposal until the last probe finishes.
    /// </remarks>
    private static void DisposeGateWhenProbesComplete(SemaphoreSlim gate, Task<DiagnosticProbeOutcome>[] tasks)
    {
        if (tasks.All(static task => task.IsCompleted))
        {
            gate.Dispose();
            return;
        }

        _ = Task.WhenAll(tasks).ContinueWith(
            static (_, state) => ((SemaphoreSlim) state!).Dispose(),
            gate,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
