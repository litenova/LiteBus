using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Extensions.Diagnostics.HealthChecks.IntegrationTests;

public sealed class LiteBusHealthCheckIntegrationTests
{
    [Fact]
    public async Task AddLiteBus_WhenDiagnosticProbeIsUnhealthy_ShouldReportUnhealthyHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.AddDiagnosticCheck<UnhealthyDiagnosticCheck>("litebus.test.unhealthy");
            });
        });

        services.AddHealthChecks().AddLiteBus();

        await using var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries.Should().ContainKey("litebus");
        report.Entries["litebus"].Status.Should().Be(HealthStatus.Unhealthy);

        report.Entries["litebus"].Description.Should()
            .Be("One or more LiteBus diagnostic probes reported unhealthy status.");

        report.Entries["litebus"].Data.Should().ContainKey("probes");
    }

    [Fact]
    public async Task AddLiteBus_WhenAllDiagnosticProbesAreHealthy_ShouldReportHealthyHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.AddDiagnosticCheck<HealthyDiagnosticCheck>("litebus.test.healthy");
            });
        });

        services.AddHealthChecks().AddLiteBus();

        await using var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries["litebus"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["litebus"].Description.Should().Be("All LiteBus diagnostic probes succeeded.");
    }

    [Fact]
    public async Task AddLiteBus_WhenNoDiagnosticProbesAreRegistered_ShouldReportDegradedHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });
        });

        services.AddHealthChecks().AddLiteBus();

        await using var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Degraded);
        report.Entries["litebus"].Status.Should().Be(HealthStatus.Degraded);
        report.Entries["litebus"].Description.Should().Be("No LiteBus diagnostic probes are registered.");
    }

    private sealed class UnhealthyDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.test.unhealthy";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "Probe failed for integration test."));
        }
    }

    private sealed class HealthyDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.test.healthy";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "Probe succeeded for integration test."));
        }
    }
}