using System.Collections;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Tests for the Marten audit module: chain registration and the time-based checkpoint loop.
/// </summary>
public sealed class MartenAuditChainModuleTests
{
    [Fact]
    public void Build_with_a_time_based_policy_registers_the_checkpoint_loop()
    {
        var configuration = new TestModuleConfiguration();

        new AuditingModule(auditing => auditing
            .ForCommands()
            .UseMartenStore()
            .UseHashChain(chain => chain.CheckpointWhen(CheckpointPolicies.Every(TimeSpan.FromMinutes(1)))))
            .Build(configuration);

        configuration.BackgroundServices.Should()
            .BeEquivalentTo<Type>([typeof(AuditLinkSweepBackgroundService), typeof(CheckpointPublishBackgroundService)]);
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditChain));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditTrail));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditChainStore<MartenAuditRecord>));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditRecordMapper<MartenAuditRecord>));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditSegmenter<MartenAuditRecord>));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(AuditingOptions));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(AuditHashChainOptions));
    }

    [Fact]
    public void Build_with_a_store_and_no_chain_registers_the_trail_only()
    {
        var configuration = new TestModuleConfiguration();

        new AuditingModule(auditing => auditing.ForCommands().UseMartenStore()).Build(configuration);

        configuration.BackgroundServices.Should().BeEmpty();
        configuration.DependencyRegistry.Should().NotContain(descriptor => descriptor.DependencyType == typeof(IAuditChain));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(MartenAuditTrail<MartenAuditRecord>));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditTrail));
    }

    [Fact]
    public void AddAuditing_with_the_marten_store_registers_the_module()
    {
        var registry = new FakeModuleRegistry();

        registry.AddAuditing(auditing => auditing.ForCommands().UseMartenStore().UseHashChain(_ => { }));

        registry.Modules.Should().ContainSingle().Subject.Should().BeOfType<AuditingModule>();

        registry.AddAuditing(auditing => auditing.ForCommands().UseMartenStore());

        registry.Modules.Should().ContainSingle();
    }

    private sealed class FakeModuleRegistry : IModuleRegistry
    {
        public List<IModule> Modules { get; } = [];

        public IModuleRegistry Register(IModule module)
        {
            Modules.Add(module);

            return this;
        }

        public bool IsModuleRegistered<T>()
            where T : IModule
        {
            return Modules.OfType<T>().Any();
        }

        public T? Find<T>()
            where T : class, IModule
        {
            return Modules.OfType<T>().FirstOrDefault();
        }

        public IReadOnlyList<ModuleDescriptor> BuildOrder()
        {
            return [];
        }
    }

    private sealed class TestModuleConfiguration : IModuleConfiguration
    {
        private readonly List<Type> _startupTasks = [];
        private readonly List<Type> _backgroundServices = [];
        private readonly List<DiagnosticCheckDescriptor> _diagnosticChecks = [];
        private readonly List<Action> _compositionValidations = [];
        private readonly Dictionary<Type, object> _contexts = [];

        /// <summary>
        ///     Initializes a new instance of the <see cref="TestModuleConfiguration" /> class with an empty message
        ///     registry, which the auditing module needs to register its completion handlers.
        /// </summary>
        public TestModuleConfiguration()
        {
            SetContext<IMessageRegistry>(MessageRegistryFactory.Create());
        }

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
            return (T) _contexts[typeof(T)];
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
                context = (T) stored;
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
                return (T) stored;
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
