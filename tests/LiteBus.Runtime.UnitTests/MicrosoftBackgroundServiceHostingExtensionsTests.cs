using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.UnitTests;

public sealed class MicrosoftBackgroundServiceHostingExtensionsTests
{
    [Fact]
    public void RegisterBackgroundServices_WhenImplementationTypeRepeated_ShouldRegisterSingleHostedService()
    {
        var services = new ServiceCollection();

        services.RegisterBackgroundServices([typeof(RecordingBackgroundService), typeof(RecordingBackgroundService)]);

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(RecordingBackgroundService)).Should().Be(1);
    }

    [Fact]
    public void RegisterBackgroundServices_WhenServicesNull_ShouldThrow()
    {
        var act = () => MicrosoftBackgroundServiceHostingExtensions.RegisterBackgroundServices(null!, []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterBackgroundServices_WhenBackgroundServicesNull_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.RegisterBackgroundServices(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService()
    {
        var services = new ServiceCollection();
        services.RegisterBackgroundServices([typeof(RecordingBackgroundService)]);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();

        using var cts = new CancellationTokenSource();
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(50, cts.Token);
        await hostedService.StopAsync(CancellationToken.None);

        provider.GetRequiredService<RecordingBackgroundService>().ExecuteCount.Should().BeGreaterThan(0);
    }

    private sealed class RecordingBackgroundService : IBackgroundService
    {
        public int ExecuteCount { get; private set; }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ExecuteCount++;
                await Task.Delay(10, stoppingToken);
            }
        }
    }
}
