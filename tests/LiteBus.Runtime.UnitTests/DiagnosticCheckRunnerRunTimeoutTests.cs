using System.Diagnostics;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticCheckRunnerRunTimeoutTests
{
    [Fact]
    public async Task RunAsync_WithEightHangingProbes_ShouldCompleteWithinComputedBudget()
    {
        var timeout = TimeSpan.FromMilliseconds(250);
        var options = new DiagnosticCheckRunOptions { MaxParallelism = 4, Timeout = timeout };
        var computedBudget = TimeSpan.FromTicks(2 * timeout.Ticks);
        var descriptors = Enumerable.Repeat(
            new DiagnosticCheckDescriptor(typeof(HangingDiagnosticCheck), "test.hanging"),
            8).ToList();

        var manifest = new LiteBusHostManifest([], [], descriptors);
        var services = new ServiceCollection()
            .AddSingleton<HangingDiagnosticCheck>()
            .BuildServiceProvider();

        var stopwatch = Stopwatch.StartNew();
        var result = await DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes: false, options).ConfigureAwait(false);
        stopwatch.Stop();

        result.Status.Should().Be(DiagnosticAggregateStatus.Unhealthy);
        result.Probes.Should().HaveCount(8);
        result.Probes.Should().OnlyContain(probe => probe.Description == "The diagnostic check timed out.");
        stopwatch.Elapsed.Should().BeLessThan(computedBudget + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_WhenRunTimeoutExpires_ShouldReportBudgetAndEndTokenObservingProbe()
    {
        var probe = new BlockingDiagnosticCheck();
        var manifest = new LiteBusHostManifest(
            [],
            [],
            [new DiagnosticCheckDescriptor(typeof(BlockingDiagnosticCheck), "test.blocking")]);

        var services = new ServiceCollection()
            .AddSingleton(probe)
            .BuildServiceProvider();

        var options = new DiagnosticCheckRunOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            RunTimeout = TimeSpan.FromMilliseconds(100)
        };

        var result = await DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes: false, options).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticAggregateStatus.Unhealthy);
        result.Probes.Should().ContainSingle().Which.Description.Should().Be("The diagnostic run exceeded its budget.");
        await probe.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private sealed class HangingDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "test.hanging";

        public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

            return new DiagnosticResult(DiagnosticStatus.Healthy, "Unexpected completion.");
        }
    }

    private sealed class BlockingDiagnosticCheck : IDiagnosticCheck
    {
        public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "test.blocking";

        public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }

            return new DiagnosticResult(DiagnosticStatus.Healthy, "Unexpected completion.");
        }
    }
}
