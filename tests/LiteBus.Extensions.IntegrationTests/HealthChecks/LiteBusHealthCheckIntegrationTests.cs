using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Extensions.IntegrationTests.HealthChecks;

public sealed class LiteBusHealthCheckIntegrationTests
{
    [Fact]
    public async Task AddLiteBus_WhenDiagnosticProbeIsUnhealthy_ShouldReportUnhealthyHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddInbox(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.AddDiagnosticCheck<UnhealthyDiagnosticCheck>("litebus.test.unhealthy");
            });
        });

        services.AddHealthChecks().AddLiteBus();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var report = await healthCheckService.CheckHealthAsync().ConfigureAwait(false);

            report.Status.Should().Be(HealthStatus.Unhealthy);
            report.Entries.Should().ContainKey("litebus");
            report.Entries["litebus"].Status.Should().Be(HealthStatus.Unhealthy);

            report.Entries["litebus"].Description.Should()
                .Be("One or more LiteBus diagnostic probes reported unhealthy status.");

            report.Entries["litebus"].Data.Should().ContainKey("probes");
        }
    }

    [Fact]
    public async Task AddLiteBus_WhenAllDiagnosticProbesAreHealthy_ShouldReportHealthyHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddInbox(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.AddDiagnosticCheck<HealthyDiagnosticCheck>("litebus.test.healthy");
            });
        });

        services.AddHealthChecks().AddLiteBus();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var report = await healthCheckService.CheckHealthAsync().ConfigureAwait(false);

            report.Status.Should().Be(HealthStatus.Healthy);
            report.Entries["litebus"].Status.Should().Be(HealthStatus.Healthy);
            report.Entries["litebus"].Description.Should().Be("All LiteBus diagnostic probes succeeded.");

            var readinessReport = await healthCheckService
                .CheckHealthAsync(registration => registration.Tags.Contains("ready"))
                .ConfigureAwait(false);

            readinessReport.Entries.Should().ContainKey("litebus");
        }
    }

    [Fact]
    public async Task AddLiteBus_WhenProbeExceedsConfiguredTimeout_ShouldReportUnhealthyHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddInbox(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.AddDiagnosticCheck<BlockingDiagnosticCheck>("litebus.test.blocking");
            });
        });

        services.AddHealthChecks().AddLiteBus(options =>
            options.DiagnosticChecks = new DiagnosticCheckRunOptions
            {
                Timeout = TimeSpan.FromMilliseconds(20),
                MaxParallelism = 1
            });

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var report = await healthCheckService.CheckHealthAsync().ConfigureAwait(false);

            report.Status.Should().Be(HealthStatus.Unhealthy);
            report.Entries["litebus"].Status.Should().Be(HealthStatus.Unhealthy);
        }
    }

    [Fact]
    public async Task AddLiteBus_WhenNoDiagnosticProbesAndFailHealthWhenNoProbesIsFalse_ShouldReportHealthyHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
        });

        services.AddHealthChecks().AddLiteBus(options => options.FailHealthWhenNoProbes = false);

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var report = await healthCheckService.CheckHealthAsync().ConfigureAwait(false);

            report.Status.Should().Be(HealthStatus.Healthy);
            report.Entries["litebus"].Status.Should().Be(HealthStatus.Healthy);
            report.Entries["litebus"].Description.Should().Be("No LiteBus diagnostic probes are registered.");
        }
    }

    [Fact]
    public async Task AddLiteBus_WhenNoDiagnosticProbesAreRegistered_ShouldReportDegradedHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
        });

        services.AddHealthChecks().AddLiteBus();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var report = await healthCheckService.CheckHealthAsync().ConfigureAwait(false);

            report.Status.Should().Be(HealthStatus.Degraded);
            report.Entries["litebus"].Status.Should().Be(HealthStatus.Degraded);
            report.Entries["litebus"].Description.Should().Be("No LiteBus diagnostic probes are registered.");
        }
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

    private sealed class BlockingDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.test.blocking";

        public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new DiagnosticResult(DiagnosticStatus.Healthy, "Unexpected completion.");
        }
    }
}
