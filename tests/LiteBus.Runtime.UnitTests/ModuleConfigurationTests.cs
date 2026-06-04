using System.Collections;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleConfigurationTests
{
    [Fact]
    public void RegisterStartupTask_ShouldPreserveFirstRegistrationOrderAndDeduplicate()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        configuration.RegisterStartupTask(typeof(RecordingStartupTask));
        configuration.RegisterStartupTask(typeof(RecordingStartupTask));

        configuration.StartupTasks.Should().Equal(typeof(RecordingStartupTask));
    }

    [Fact]
    public void RegisterBackgroundService_ShouldPreserveFirstRegistrationOrderAndDeduplicate()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        configuration.RegisterBackgroundService(typeof(RecordingContinuousBackgroundService));
        configuration.RegisterBackgroundService(typeof(RecordingContinuousBackgroundService));

        configuration.BackgroundServices.Should().Equal(typeof(RecordingContinuousBackgroundService));
    }

    [Fact]
    public void RegisterBackgroundService_WhenTypeImplementsStartupTask_ShouldThrow()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var act = () => configuration.RegisterBackgroundService(typeof(RecordingStartupTask));

        act.Should().Throw<ArgumentException>();
    }

    private sealed class RecordingStartupTask : IStartupTask
    {
        /// <inheritdoc />
        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingContinuousBackgroundService : IBackgroundService
    {
        /// <inheritdoc />
        public Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestMessageRegistry : IMessageRegistry
    {
        /// <inheritdoc />
        public int Count => 0;

        /// <inheritdoc />
        public IReadOnlyList<IHandlerDescriptor> Handlers => [];

        /// <inheritdoc />
        public IMessageDescriptor? Find(Type messageType) => null;

        /// <inheritdoc />
        public void Register(Type type)
        {
        }

        /// <inheritdoc />
        public IEnumerator<IMessageDescriptor> GetEnumerator()
        {
            return Enumerable.Empty<IMessageDescriptor>().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void GetContext_WhenMissing_ShouldThrowLiteBusConfigurationException()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var act = () => configuration.GetContext<FoundationModule>();

        act.Should().Throw<LiteBusConfigurationException>();
    }

    [Fact]
    public void SetContext_ShouldAllowGetContext()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var module = new FoundationModule();

        configuration.SetContext(module);

        configuration.GetContext<FoundationModule>().Should().BeSameAs(module);
    }

    [Fact]
    public void SetContext_WithLaterValue_ShouldOverwriteExistingContext()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var first = new FoundationModule();
        var second = new FoundationModule();

        configuration.SetContext(first);
        configuration.SetContext(second);

        configuration.GetContext<FoundationModule>().Should().BeSameAs(second);
    }

    [Fact]
    public void TryGetContext_WhenMissing_ShouldReturnFalse()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var found = configuration.TryGetContext<FoundationModule>(out var context);

        found.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void GetOrCreateContext_ForMessageRegistry_ShouldCreateSeparateInstancesPerConfiguration()
    {
        var firstConfiguration = new ModuleConfiguration(new DependencyRegistry());
        var secondConfiguration = new ModuleConfiguration(new DependencyRegistry());

        var firstRegistry = firstConfiguration.GetOrCreateContext<IMessageRegistry>(() => new TestMessageRegistry());
        var secondRegistry = secondConfiguration.GetOrCreateContext<IMessageRegistry>(() => new TestMessageRegistry());

        firstRegistry.Should().NotBeSameAs(secondRegistry);
    }

    [Fact]
    public void GetOrCreateContext_ShouldCreateOnce()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var createCount = 0;

        var first = configuration.GetOrCreateContext(() =>
        {
            createCount++;
            return new FoundationModule();
        });
        var second = configuration.GetOrCreateContext(() =>
        {
            createCount++;
            return new FoundationModule();
        });

        first.Should().BeSameAs(second);
        createCount.Should().Be(1);
    }
}
