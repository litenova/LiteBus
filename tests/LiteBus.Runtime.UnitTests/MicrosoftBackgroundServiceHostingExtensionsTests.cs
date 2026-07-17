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

         var provider = services.BuildServiceProvider();
         await using (provider.ConfigureAwait(false))
         {
        var hostedServices = provider.GetServices<IHostedService>().ToList();

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

        provider.GetRequiredService<RecordingBackgroundService>().ExecuteCount.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task RegisterBackgroundServices_WhenStartupTaskThrows_ShouldNotStartBackgroundServices()
    {
        var services = new ServiceCollection();

        services.RegisterBackgroundServices(
            [typeof(FailingStartupTask)],
            [typeof(StartupFailureBackgroundService)]);

         var provider = services.BuildServiceProvider();
         await using (provider.ConfigureAwait(false))
         {
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        using var cts = new CancellationTokenSource();

        Exception? startupFailure = null;

        foreach (var hostedService in hostedServices)
        {
            try
            {
                await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                startupFailure = exception;
            }
        }

        startupFailure.Should().NotBeNull();

        await Task.Delay(50, cts.Token).ConfigureAwait(false);

        provider.GetRequiredService<StartupFailureBackgroundService>().StartedAfterStartup.Should().BeFalse();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    [Fact]
    public async Task RegisterBackgroundServices_WhenStartupTaskRegisteredFirst_ShouldCompleteStartupBeforeContinuousLoop()
    {
        RecordingStartupTaskState.StartupCompleted = false;

        var services = new ServiceCollection();

        services.RegisterBackgroundServices(
            [typeof(RecordingStartupTask)],
            [typeof(OrderedContinuousBackgroundService)]);

         var provider = services.BuildServiceProvider();
         await using (provider.ConfigureAwait(false))
         {
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        using var cts = new CancellationTokenSource();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cts.Token).ConfigureAwait(false);
        }

        await Task.Delay(50, cts.Token).ConfigureAwait(false);

        provider.GetRequiredService<OrderedContinuousBackgroundService>().StartedAfterStartup.Should().BeTrue();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        }
    }

    [Fact]
    public async Task RegisterBackgroundServices_WhenBackgroundServiceFaults_ShouldStopApplication()
    {
        var services = new ServiceCollection();
        using var lifetime = new RecordingHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.RegisterBackgroundServices(
            [],
            [typeof(FaultingBackgroundService), typeof(RecordingBackgroundService)]);

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var hostedService = provider.GetServices<IHostedService>().Single();
            var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = lifetime.ApplicationStopping.Register(() => stopRequested.TrySetResult());

            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);

            lifetime.StopApplicationCallCount.Should().Be(1);
        }
    }

    private sealed class FailingStartupTask : IStartupTask
    {
        /// <inheritdoc />
        public Task RunAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Startup task failed for test.");
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
                await Task.Delay(10, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class StartupFailureBackgroundService : IBackgroundService
    {
        /// <summary>
        ///     Gets a value indicating whether the continuous loop started after startup tasks completed.
        /// </summary>
        public bool StartedAfterStartup { get; private set; }

        /// <inheritdoc />
        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            StartedAfterStartup = true;

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10, stoppingToken).ConfigureAwait(false);
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
                await Task.Delay(10, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
