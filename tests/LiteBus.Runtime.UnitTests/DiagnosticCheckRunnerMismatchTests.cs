using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticCheckRunnerMismatchTests
{
    [Fact]
    public async Task RunAsync_WhenProbeNameDiffersFromDescriptor_ShouldReportMismatchMessage()
    {
        var manifest = new LiteBusHostManifest(
            [],
            [],
            [new DiagnosticCheckDescriptor(typeof(MismatchedDiagnosticCheck), "expected.name")]);

        var services = new ServiceCollection()
            .AddSingleton<MismatchedDiagnosticCheck>()
            .BuildServiceProvider();

        var result = await DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes: false).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticAggregateStatus.Unhealthy);
        var outcome = result.Probes.Should().ContainSingle().Subject;
        outcome.Description.Should().Contain("expected.name").And.Contain("actual.name");
    }

    [Fact]
    public async Task RunAsync_WhenRunTimeoutIsNegative_ShouldThrowArgumentOutOfRangeException()
    {
        var manifest = new LiteBusHostManifest([], [], []);
        var services = new ServiceCollection().BuildServiceProvider();

        var act = () => DiagnosticCheckRunner.RunAsync(
            manifest,
            services,
            failHealthWhenNoProbes: false,
            new DiagnosticCheckRunOptions { RunTimeout = TimeSpan.FromSeconds(-1) });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    private sealed class MismatchedDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "actual.name";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "ok"));
    }
}
