using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticCheckRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenNoProbesAndFailHealthWhenNoProbesIsTrue_ShouldReportDegraded()
    {
        var manifest = new LiteBusHostManifest([], [], []);
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes: true).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticAggregateStatus.Degraded);
        result.Probes.Should().ContainSingle(probe => probe.Name == "litebus.probes");
    }

    [Fact]
    public async Task RunAsync_WhenProbeIsUnhealthy_ShouldReportUnhealthy()
    {
        var manifest = new LiteBusHostManifest(
            [],
            [],
            [new DiagnosticCheckDescriptor(typeof(UnhealthyDiagnosticCheck), "test.unhealthy")]);

        var services = new ServiceCollection()
            .AddSingleton<UnhealthyDiagnosticCheck>()
            .BuildServiceProvider();

        var result = await DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes: false).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticAggregateStatus.Unhealthy);
    }

    [Fact]
    public async Task RunAsync_WhenProbeIsDegraded_ShouldReportDegraded()
    {
        var manifest = new LiteBusHostManifest(
            [],
            [],
            [new DiagnosticCheckDescriptor(typeof(DegradedDiagnosticCheck), "test.degraded")]);

        var services = new ServiceCollection()
            .AddSingleton<DegradedDiagnosticCheck>()
            .BuildServiceProvider();

        var result = await DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes: false).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticAggregateStatus.Degraded);
    }

    private sealed class UnhealthyDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "test.unhealthy";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Unhealthy, "Unhealthy probe."));
        }
    }

    private sealed class DegradedDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "test.degraded";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Degraded, "Degraded probe."));
        }
    }
}
