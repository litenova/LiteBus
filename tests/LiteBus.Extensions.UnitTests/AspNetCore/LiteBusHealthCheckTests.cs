using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Extensions.UnitTests.AspNetCore;

public sealed class LiteBusHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WithRegisteredProbes_ShouldIncludeProbeNamesInData()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<FirstProbe>()
            .AddSingleton<SecondProbe>()
            .BuildServiceProvider();
        var manifest = new LiteBusHostManifest(
            [],
            [],
            [
                new DiagnosticCheckDescriptor(typeof(FirstProbe), "test.first"),
                new DiagnosticCheckDescriptor(typeof(SecondProbe), "test.second")
            ]);
        var check = new LiteBusHealthCheck(manifest, provider, new LiteBusHealthCheckOptions());

        var result = await check.CheckHealthAsync(new HealthCheckContext()).ConfigureAwait(false);

        var probes = result.Data["probes"].Should().BeOfType<Dictionary<string, object?>[]>().Subject;
        probes.Should().HaveCount(2);
        string[] expectedNames = ["test.first", "test.second"];
        probes.Select(probe => probe["name"] as string).Should().BeEquivalentTo(expectedNames);
    }

    [Fact]
    public void AddLiteBus_WithNegativeRunTimeout_ShouldThrowArgumentOutOfRangeException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHealthChecks().AddLiteBus(options =>
            options.DiagnosticChecks = new DiagnosticCheckRunOptions { RunTimeout = TimeSpan.FromSeconds(-1) });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class FirstProbe : IDiagnosticCheck
    {
        public string Name => "test.first";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "First probe succeeded."));
        }
    }

    private sealed class SecondProbe : IDiagnosticCheck
    {
        public string Name => "test.second";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Unhealthy, "Second probe failed."));
        }
    }
}
