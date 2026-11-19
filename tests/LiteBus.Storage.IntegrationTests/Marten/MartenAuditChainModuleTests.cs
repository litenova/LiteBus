using System.Collections;
using LiteBus.Commands;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Tests for the Marten audit module: chain registration and the time-based checkpoint loop.
/// </summary>
public sealed class MartenAuditChainModuleTests
{
    [Fact]
    public void Build_with_a_time_based_policy_registers_the_checkpoint_loop()
    {
        var options = new MartenAuditTrailOptions();
        options.UseHashChain(chain => chain.CheckpointWhen(CheckpointPolicies.Every(TimeSpan.FromMinutes(1))));
        var configuration = new TestModuleConfiguration();

        new MartenAuditModule(options).Build(configuration);

        configuration.BackgroundServices.Should().Contain(typeof(MartenAuditCheckpointBackgroundService));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditChain));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(ICheckpointSource));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(MartenAuditTrailOptions));
    }

    [Fact]
    public void Build_with_a_record_policy_skips_the_checkpoint_loop()
    {
        var options = new MartenAuditTrailOptions().UseHashChain();
        var configuration = new TestModuleConfiguration();

        new MartenAuditModule(options).Build(configuration);

        configuration.BackgroundServices.Should().BeEmpty();
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditChain));
    }

    [Fact]
    public void Build_without_the_hash_chain_throws()
    {
        var configuration = new TestModuleConfiguration();

        var build = () => new MartenAuditModule(new MartenAuditTrailOptions()).Build(configuration);

        build.Should().Throw<LiteBusConfigurationException>().WithMessage("*UseHashChain*");
    }

    [Fact]
    public void UseMartenAuditChain_registers_the_module_with_defaults()
    {
        var builder = new CommandModuleBuilder();

        builder.UseMartenAuditChain();

        builder.CollectSubModules().Should().ContainSingle().Subject.Should().BeOfType<MartenAuditModule>();
    }

    private sealed class TestModuleConfiguration : IModuleConfiguration
    {
        private readonly List<Type> _startupTasks = [];
        private readonly List<Type> _backgroundServices = [];
        private readonly List<DiagnosticCheckDescriptor> _diagnosticChecks = [];
        private readonly List<Action> _compositionValidations = [];
        private readonly Dictionary<Type, object> _contexts = [];

        public IDependencyRegistry DependencyRegistry { get; } = new TestDependencyRegistry();

        public IReadOnlyList<Type> StartupTasks => _startupTasks;

        public IReadOnlyList<Type> BackgroundServices => _backgroundServices;

        public IReadOnlyList<DiagnosticCheckDescriptor> DiagnosticChecks => _diagnosticChecks;

        public IReadOnlyList<Action> CompositionValidations => _compositionValidations;

        public void RegisterStartupTask(Type implementationType)
        {
            _startupTasks.Add(implementationType);
        }

        public void RegisterBackgroundService(Type implementationType)
        {
            _backgroundServices.Add(implementationType);
        }

        public void RegisterDiagnosticCheck(Type implementationType, string name)
        {
            _diagnosticChecks.Add(new DiagnosticCheckDescriptor(implementationType, name));
        }

        public void RegisterCompositionValidation(Action validate)
        {
            _compositionValidations.Add(validate);
        }

        public T GetContext<T>()
            where T : class
        {
            return (T)_contexts[typeof(T)];
        }

        public void SetContext<T>(T context)
            where T : class
        {
            _contexts[typeof(T)] = context;
        }

        public bool TryGetContext<T>(out T? context)
            where T : class
        {
            if (_contexts.TryGetValue(typeof(T), out var stored))
            {
                context = (T)stored;
                return true;
            }

            context = null;
            return false;
        }

        public T GetOrCreateContext<T>(Func<T> factory)
            where T : class
        {
            if (_contexts.TryGetValue(typeof(T), out var stored))
            {
                return (T)stored;
            }

            var created = factory();
            _contexts[typeof(T)] = created;
            return created;
        }
    }

    private sealed class TestDependencyRegistry : IDependencyRegistry
    {
        private readonly List<DependencyDescriptor> _descriptors = [];

        public int Count => _descriptors.Count;

        public void Register(DependencyDescriptor descriptor)
        {
            _descriptors.Add(descriptor);
        }

        public void RegisterCollection(DependencyDescriptor descriptor)
        {
            _descriptors.Add(descriptor);
        }

        public IEnumerator<DependencyDescriptor> GetEnumerator()
        {
            return _descriptors.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
