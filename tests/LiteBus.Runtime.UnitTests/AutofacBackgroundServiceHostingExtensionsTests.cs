using Autofac;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Autofac.Hosting;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.UnitTests;

public sealed class AutofacBackgroundServiceHostingExtensionsTests
{
    [Fact]
    public void RegisterBackgroundServices_WhenImplementationTypeRepeated_ShouldResolveSingleHostedService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterBackgroundServices([], [typeof(RecordingBackgroundService), typeof(RecordingBackgroundService)]);

        using var container = builder.Build();

        container.Resolve<IEnumerable<IHostedService>>().Should().HaveCount(1);
        container.Resolve<RecordingBackgroundService>().Should().NotBeNull();
    }

    [Fact]
    public void RegisterBackgroundServices_WhenBuilderNull_ShouldThrow()
    {
        var act = () => AutofacBackgroundServiceHostingExtensions.RegisterBackgroundServices(null!, [], []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterBackgroundServices_WhenBackgroundServicesNull_ShouldThrow()
    {
        var builder = new ContainerBuilder();

        var act = () => builder.RegisterBackgroundServices([], null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterBackgroundServices([], [typeof(RecordingBackgroundService)]);

        await using var container = builder.Build();

        var hostedServices = container.Resolve<IEnumerable<IHostedService>>().ToList();
        using var cts = new CancellationTokenSource();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cts.Token);
        }

        await Task.Delay(50, cts.Token);

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        container.Resolve<RecordingBackgroundService>().ExecuteCount.Should().BeGreaterThan(0);
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