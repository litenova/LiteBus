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

         var container = builder.Build();
         await using (container.ConfigureAwait(false))
         {

        var hostedServices = container.Resolve<IEnumerable<IHostedService>>().ToList();
        using var cts = new CancellationTokenSource();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        }

        await Task.Delay(50, cts.Token).ConfigureAwait(false);

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        container.Resolve<RecordingBackgroundService>().ExecuteCount.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task RegisterBackgroundServices_WhenBackgroundServiceFaults_ShouldStopApplication()
    {
        var builder = new ContainerBuilder();
        using var lifetime = new RecordingHostApplicationLifetime();
        builder.RegisterInstance(lifetime).As<IHostApplicationLifetime>();
        builder.RegisterBackgroundServices(
            [],
            [typeof(FaultingBackgroundService), typeof(RecordingBackgroundService)]);

        var container = builder.Build();
        await using (container.ConfigureAwait(false))
        {
            var hostedService = container.Resolve<IEnumerable<IHostedService>>().Single();
            var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = lifetime.ApplicationStopping.Register(() => stopRequested.TrySetResult());

            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

            lifetime.StopApplicationCallCount.Should().Be(1);
        }
    }

    private sealed class FaultingBackgroundService : IBackgroundService
    {
        /// <inheritdoc />
        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("Background service failed for test.");
        }
    }

    private sealed class RecordingBackgroundService : IBackgroundService
    {
        public int ExecuteCount { get; private set; }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ExecuteCount++;
                await Task.Delay(10, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
