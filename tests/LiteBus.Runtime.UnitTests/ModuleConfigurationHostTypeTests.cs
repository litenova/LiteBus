using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleConfigurationHostTypeTests
{
    [Fact]
    public void RegisterStartupTask_WhenTypeImplementsBackgroundService_ShouldThrowArgumentException()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var act = () => configuration.RegisterStartupTask(typeof(DualPurposeHostType));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("implementationType");
    }

    [Fact]
    public void RegisterBackgroundService_WhenTypeImplementsStartupTask_ShouldThrowArgumentException()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var act = () => configuration.RegisterBackgroundService(typeof(DualPurposeHostType));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("implementationType");
    }

    [Fact]
    public void RegisterStartupTask_WhenTypeImplementsOnlyStartupTask_ShouldRegister()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        configuration.RegisterStartupTask(typeof(StartupOnlyTask));

        configuration.StartupTasks.Should().ContainSingle(type => type == typeof(StartupOnlyTask));
    }

    [Fact]
    public void RegisterBackgroundService_WhenTypeImplementsOnlyBackgroundService_ShouldRegister()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        configuration.RegisterBackgroundService(typeof(BackgroundOnlyService));

        configuration.BackgroundServices.Should().ContainSingle(type => type == typeof(BackgroundOnlyService));
    }

    private sealed class DualPurposeHostType : IStartupTask, IBackgroundService
    {
        /// <inheritdoc />
        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StartupOnlyTask : IStartupTask
    {
        /// <inheritdoc />
        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class BackgroundOnlyService : IBackgroundService
    {
        /// <inheritdoc />
        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }
}
