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

        services.RegisterBackgroundServices([], [typeof(RecordingBackgroundService), typeof(RecordingBackgroundService)]);

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(RecordingBackgroundService)).Should().Be(1);
    }

    [Fact]
    public void RegisterBackgroundServices_WhenServicesNull_ShouldThrow()
    {
        var act = () => MicrosoftBackgroundServiceHostingExtensions.RegisterBackgroundServices(null!, [], []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterBackgroundServices_WhenStartupTasksNull_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.RegisterBackgroundServices(null!, []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterBackgroundServices_WhenBackgroundServicesNull_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.RegisterBackgroundServices([], null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterBackgroundServices_ShouldExecuteUnderlyingBackgroundService()
    {
        var services = new ServiceCollection();
        services.RegisterBackgroundServices([], [typeof(RecordingBackgroundService)]);

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

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

        provider.GetRequiredService<RecordingBackgroundService>().ExecuteCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RegisterBackgroundServices_WhenStartupTaskRegisteredFirst_ShouldCompleteStartupBeforeContinuousLoop()
    {
        RecordingStartupTaskState.StartupCompleted = false;

        var services = new ServiceCollection();

        services.RegisterBackgroundServices(
            [typeof(RecordingStartupTask)],
            [typeof(OrderedContinuousBackgroundService)]);

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        using var cts = new CancellationTokenSource();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cts.Token);
        }

        await Task.Delay(50, cts.Token);

        provider.GetRequiredService<OrderedContinuousBackgroundService>().StartedAfterStartup.Should().BeTrue();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingStartupTask : IStartupTask
    {
        /// <inheritdoc />
        public Task RunAsync(CancellationToken cancellationToken)
        {
            RecordingStartupTaskState.StartupCompleted = true;
            return Task.CompletedTask;
        }
    }

    private static class RecordingStartupTaskState
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the startup task has completed.
        /// </summary>
        public static bool StartupCompleted { get; set; }
    }

    private sealed class OrderedContinuousBackgroundService : IBackgroundService
    {
        /// <summary>
        ///     Gets a value indicating whether the continuous loop started after the startup task completed.
        /// </summary>
        public bool StartedAfterStartup { get; private set; }

        /// <inheritdoc />
        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            StartedAfterStartup = RecordingStartupTaskState.StartupCompleted;

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10, stoppingToken);
            }
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
                await Task.Delay(10, stoppingToken);
            }
        }
    }
}