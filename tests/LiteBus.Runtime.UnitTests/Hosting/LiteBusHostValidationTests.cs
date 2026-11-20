using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.Hosting;

/// <summary>
///     Covers host validation that resolves LiteBus registrations without starting a host.
/// </summary>
public sealed class LiteBusHostValidationTests
{
    /// <summary>
    ///     Confirms a composition with one resolvable background service passes validation.
    /// </summary>
    [Fact]
    public void Validate_ValidCompositionWithBackgroundService_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.Modules.Register(
            new RegistrationModule(configuration =>
                configuration.RegisterBackgroundService(typeof(ValidBackgroundService)))));

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a singleton background service that captures a scoped service is reported by name.
    /// </summary>
    [Fact]
    public void Validate_SingletonBackgroundServiceDependingOnScopedService_ShouldReportThatBackgroundService()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.Modules.Register(new RegistrationModule(configuration =>
        {
            configuration.RegisterBackgroundService(typeof(ScopedDependentBackgroundService));
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(IScopedDependency),
                typeof(ScopedDependency),
                InstanceLifetime.Scoped));
        })));

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().Contain(failure =>
            failure.Contains(nameof(ScopedDependentBackgroundService), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms a background service with an unregistered constructor dependency is reported by name.
    /// </summary>
    [Fact]
    public void Validate_BackgroundServiceWithUnregisteredDependency_ShouldReportThatDependency()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.Modules.Register(new RegistrationModule(configuration =>
            configuration.RegisterBackgroundService(typeof(MissingDependencyBackgroundService)))));

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().Contain(failure =>
            failure.Contains(nameof(IMissingDependency), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms two independent composition problems are both reported from one validation run.
    /// </summary>
    [Fact]
    public void Validate_TwoIndependentProblems_ShouldListBoth()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.Modules.Register(new RegistrationModule(configuration =>
        {
            configuration.RegisterBackgroundService(typeof(FirstMissingDependencyBackgroundService));
            configuration.RegisterBackgroundService(typeof(SecondMissingDependencyBackgroundService));
        })));

        var act = () => LiteBusHostValidation.Validate(services);

        var failures = act.Should().Throw<LiteBusHostValidationException>().Which.Failures;
        failures.Should().Contain(failure =>
            failure.Contains(nameof(IMissingFirstDependency), StringComparison.Ordinal));
        failures.Should().Contain(failure =>
            failure.Contains(nameof(IMissingSecondDependency), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Applies a configuration action from an <see cref="IModule" /> registered through the LiteBus module API.
    /// </summary>
    private sealed class RegistrationModule : IModule
    {
        private readonly Action<IModuleConfiguration> _configure;

        public RegistrationModule(Action<IModuleConfiguration> configure)
        {
            _configure = configure;
        }

        public void Build(IModuleConfiguration configuration)
        {
            _configure(configuration);
        }
    }

    private interface IScopedDependency
    {
    }

    private sealed class ScopedDependency : IScopedDependency
    {
    }

    private interface IMissingDependency
    {
    }

    private interface IMissingFirstDependency
    {
    }

    private interface IMissingSecondDependency
    {
    }

    private sealed class ValidBackgroundService : IBackgroundService
    {
        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ScopedDependentBackgroundService : IBackgroundService
    {
        public ScopedDependentBackgroundService(IScopedDependency dependency)
        {
            _ = dependency;
        }

        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class MissingDependencyBackgroundService : IBackgroundService
    {
        public MissingDependencyBackgroundService(IMissingDependency dependency)
        {
            _ = dependency;
        }

        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FirstMissingDependencyBackgroundService : IBackgroundService
    {
        public FirstMissingDependencyBackgroundService(IMissingFirstDependency dependency)
        {
            _ = dependency;
        }

        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SecondMissingDependencyBackgroundService : IBackgroundService
    {
        public SecondMissingDependencyBackgroundService(IMissingSecondDependency dependency)
        {
            _ = dependency;
        }

        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }
}
