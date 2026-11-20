using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.InMemory;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Auditing.UnitTests.Engine;

/// <summary>
///     Verifies that the auditing module registers the capture entry trail, the chain engine, and its background
///     work.
/// </summary>
public sealed class AuditingModuleTests
{
    [Fact]
    public void Build_WithoutStore_ShouldThrowNamingStoreExtensions()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing.ForCommands()).Build(configuration);

        act.Should().Throw<AuditConfigurationException>().WithMessage("*UseInMemoryStore*");
    }

    [Fact]
    public void Build_WithChainAndNoMapper_ShouldThrowNamingStoreAs()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseHashChain(_ => { })).Build(configuration);

        // The in-memory store supplies the document default but no mapper default.
        act.Should().Throw<AuditConfigurationException>().WithMessage("*StoreAs*");
    }

    [Fact]
    public void Build_WithChainAndNoSegmenter_ShouldThrowNamingSegmentBy()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseHashChain(chain => chain
                .StoreAs<ChainTestDocument, ChainTestMapper>())).Build(configuration);

        act.Should().Throw<AuditConfigurationException>().WithMessage("*SegmentBy*");
    }

    [Fact]
    public void Build_WithMismatchedSegmenter_ShouldThrowNamingSegmentBy()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseHashChain(chain => chain
                .StoreAs<ChainTestDocument, ChainTestMapper>()
                .SegmentBy<NotASegmenter>())).Build(configuration);

        act.Should().Throw<AuditConfigurationException>().WithMessage("*SegmentBy*");
    }

    [Fact]
    public void Build_WithWithChangeAtWrite_ShouldRejectPair()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .Capture(AuditCapture.WithChange(AuditLink.AtWrite))
            .UseInMemoryStore<ChainTestDocument>()).Build(configuration);

        act.Should().Throw<AuditConfigurationException>().WithMessage("*AtWrite*");
    }

    [Fact]
    public void Build_WithUnknownScheme_ShouldThrowNamingShippedSchemes()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseHashChain(chain => chain
                .StoreAs<ChainTestDocument, ChainTestMapper>()
                .SegmentBy<ChainTestSegmenter>()
                .UseScheme("hmac-custom"))).Build(configuration);

        act.Should().Throw<AuditConfigurationException>().WithMessage("*sha256-v2*");
    }

    [Fact]
    public void Build_WithPreviousScheme_ShouldSucceedWithoutSelfTest()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseHashChain(chain => chain
                .StoreAs<ChainTestDocument, ChainTestMapper>()
                .SegmentBy<ChainTestSegmenter>()
                .UseScheme("sha256-v1"))).Build(configuration);

        act.Should().NotThrow();
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditChain));
    }

    [Fact]
    public void Build_WithChain_ShouldRegisterEngineAndBackgroundWork()
    {
        var configuration = new FakeModuleConfiguration();

        BuildChainedModule(configuration);

        var registry = configuration.DependencyRegistry;
        var closedStore = typeof(IAuditChainStore<ChainTestDocument>);

        DescriptorFor(registry, typeof(AuditingOptions)).Instance.Should().BeOfType<AuditingOptions>();
        DescriptorFor(registry, typeof(AuditHashChainOptions)).Instance.Should().BeOfType<AuditHashChainOptions>();
        DescriptorFor(registry, typeof(IAuditChain)).Factory.Should().NotBeNull();
        DescriptorFor(registry, typeof(IAuditTrail)).Factory.Should().NotBeNull();
        DescriptorFor(registry, typeof(IAuditCaptureCoordinator)).Lifetime.Should().Be(InstanceLifetime.Scoped);
        DescriptorFor(registry, typeof(IAuditLinker)).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, typeof(ICheckpointLifecycle)).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, typeof(IAuditVerifier)).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, typeof(IAuditSegmentManager)).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, typeof(IAuditHashSchemeRegistry)).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, typeof(IAuditCheckpointStore)).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, closedStore).ImplementationType.Should()
            .Be<InMemoryAuditChainStore<ChainTestDocument>>();
        DescriptorFor(registry, closedStore).Lifetime.Should().Be(InstanceLifetime.Singleton);
        DescriptorFor(registry, typeof(IAuditRecordMapper<ChainTestDocument>)).ImplementationType.Should()
            .Be<ChainTestMapper>();
        DescriptorFor(registry, typeof(IAuditSegmenter<ChainTestDocument>)).ImplementationType.Should()
            .Be<ChainTestSegmenter>();

        configuration.BackgroundServices.Should().HaveCount(2);
        configuration.BackgroundServices.Should().Contain(typeof(AuditLinkSweepBackgroundService));
        configuration.BackgroundServices.Should().Contain(typeof(CheckpointPublishBackgroundService));
    }

    [Fact]
    public void Build_WithChain_ShouldSelectOutboxCaptureVisibly()
    {
        var configuration = new FakeModuleConfiguration();

        BuildChainedModule(configuration);

        var options = (AuditingOptions) DescriptorFor(configuration.DependencyRegistry, typeof(AuditingOptions)).Instance!;
        options.Capture.Should().BeOfType<AuditCapture.AfterCommitThroughOutboxCapture>();

        var section = Sections(configuration).Should().ContainSingle().Subject;
        section.SectionName.Should().Be("auditing");

        var data = (IReadOnlyDictionary<string, object?>) section.GetData()!;
        data["captureRequested"].Should().Be(nameof(AuditCapture.WithChange));
        data["captureEffective"].Should().Be(nameof(AuditCapture.AfterCommitThroughOutbox));
        data["captureSelection"].Should().Be("automatic: the audit store cannot join the business transaction");
        data["chainEnabled"].Should().Be(true);
        data["scheme"].Should().Be("sha256-v2");
    }

    [Fact]
    public void Build_WithChain_ShouldListStoreSourceFirst()
    {
        var configuration = new FakeModuleConfiguration();

        BuildChainedModule(configuration);

        var chain = (AuditHashChainOptions) DescriptorFor(
            configuration.DependencyRegistry,
            typeof(AuditHashChainOptions)).Instance!;

        chain.SourceTypes.Should().ContainSingle()
            .Which.Should().Be<InMemoryAuditChainStore<ChainTestDocument>>();
    }

    [Fact]
    public void Build_WithoutChain_ShouldRegisterTrailOnly()
    {
        var configuration = new FakeModuleConfiguration();

        new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()).Build(configuration);

        var registry = configuration.DependencyRegistry;

        DescriptorFor(registry, typeof(IAuditTrail)).Factory.Should().NotBeNull();
        DescriptorFor(registry, typeof(IAuditCaptureCoordinator)).Lifetime.Should().Be(InstanceLifetime.Scoped);
        registry.Should().NotContain(descriptor => descriptor.DependencyType == typeof(IAuditChain));
        registry.Should().NotContain(descriptor => descriptor.DependencyType == typeof(IAuditLinker));
        configuration.BackgroundServices.Should().BeEmpty();

        var section = Sections(configuration).Should().ContainSingle().Subject;
        var data = (IReadOnlyDictionary<string, object?>) section.GetData()!;
        data["chainEnabled"].Should().Be(false);
        data["scheme"].Should().BeNull();
    }

    [Fact]
    public void Configure_CalledTwice_ShouldApplyBothCallbacksToOneBuilder()
    {
        var configuration = new FakeModuleConfiguration();
        var module = new AuditingModule(auditing => auditing
            .ForCommands()
            .Capture(AuditCapture.BeforeAndOutcome()));

        module.Configure(auditing => auditing.UseInMemoryStore<ChainTestDocument>(), _ => { });
        module.Build(configuration);

        var options = (AuditingOptions) DescriptorFor(configuration.DependencyRegistry, typeof(AuditingOptions)).Instance!;
        options.Capture.Should().BeOfType<AuditCapture.BeforeAndOutcomeCapture>();
        options.TrailType.Should().Be<InMemoryAuditTrail>();
    }

    [Fact]
    public void UsePostgreSqlStore_ShouldRegisterThePostgreSqlModuleAsAChild()
    {
        var module = new AuditingModule(auditing => auditing
            .ForCommands()
            .UsePostgreSqlStore(postgres => postgres
                .UseConnectionString("Host=localhost;Username=auditing;Password=auditing;Database=auditing"))
            .UseHashChain(_ => { }));

        List<IModule> children = [];
        module.DeclareChildren(children.Add);

        var child = children.Should().ContainSingle().Subject;
        child.Should().BeOfType<PostgreSqlAuditModule>();

        var configuration = new FakeModuleConfiguration();
        module.Build(configuration);
        child.Build(configuration);

        var registry = configuration.DependencyRegistry;
        registry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAuditTrail));
        registry.Should().Contain(descriptor => descriptor.DependencyType == typeof(PostgreSqlAuditStoreRegistration));
        registry.Should().Contain(descriptor =>
            descriptor.DependencyType == typeof(IAuditCanonicalizer<PostgreSqlAuditRecord>) && descriptor.IsLiteBusDefault);
        configuration.StartupTasks.Should().Contain(typeof(PostgreSqlAuditSchemaInitializer));
        configuration.DiagnosticChecks.Should().Contain(
            check => check.ImplementationType == typeof(PostgreSqlAuditSchemaDiagnosticCheck));
    }

    [Fact]
    public void Constructor_WithNullConfigure_ShouldThrow()
    {
        var act = () => new AuditingModule(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Configure_WithNullConfigure_ShouldThrow()
    {
        var module = new AuditingModule(_ => { });

        var act = () => module.Configure(null!, _ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Configure_WithNullRegisterChild_ShouldThrow()
    {
        var module = new AuditingModule(_ => { });

        var act = () => module.Configure(_ => { }, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DeclareChildren_WithNullRegisterChild_ShouldThrow()
    {
        var module = new AuditingModule(_ => { });

        var act = () => module.DeclareChildren(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_WithNullConfiguration_ShouldThrow()
    {
        var act = () => new AuditingModule(_ => { }).Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseInMemoryStore_CalledTwiceWithSameDocument_ShouldBeNoOp()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseInMemoryStore<ChainTestDocument>()).Build(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void UseInMemoryStore_CalledTwiceWithAnotherDocument_ShouldThrow()
    {
        var configuration = new FakeModuleConfiguration();

        var act = () => new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseInMemoryStore<OtherChainTestDocument>()).Build(configuration);

        // The stored configuration runs when the module declares its children or builds, so the conflict
        // surfaces there rather than in the constructor.
        act.Should().Throw<AuditConfigurationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void ResolvedChain_WithContainer_ShouldShareOneStoreInstance()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.Modules.AddAuditing(auditing => auditing
                    .ForCommands()
                    .UseInMemoryStore<ChainTestDocument>()
                    .UseHashChain(chain => chain
                        .StoreAs<ChainTestDocument, ChainTestMapper>()
                        .SegmentBy<ChainTestSegmenter>()));
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IAuditChain>().Should().BeOfType<AuditChain>();
        provider.GetRequiredService<IAuditTrail>().Should().BeOfType<AuditCaptureTrail>();

        var byContract = provider.GetRequiredService<IAuditChainStore<ChainTestDocument>>();
        var byConcrete = provider.GetRequiredService<InMemoryAuditChainStore<ChainTestDocument>>();

        byConcrete.Should().BeSameAs(byContract);

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAuditCaptureCoordinator>().Should().NotBeNull();
        provider.GetRequiredService<AuditingOptions>().Capture.Should()
            .BeOfType<AuditCapture.AfterCommitThroughOutboxCapture>();
    }

    private static void BuildChainedModule(FakeModuleConfiguration configuration)
    {
        new AuditingModule(auditing => auditing
            .ForCommands()
            .UseInMemoryStore<ChainTestDocument>()
            .UseHashChain(chain => chain
                .StoreAs<ChainTestDocument, ChainTestMapper>()
                .SegmentBy<ChainTestSegmenter>())).Build(configuration);
    }

    private static DependencyDescriptor DescriptorFor(IDependencyRegistry registry, Type serviceType)
    {
        return registry.First(descriptor => descriptor.DependencyType == serviceType);
    }

    private static IReadOnlyList<ICompositionSectionContributor> Sections(FakeModuleConfiguration configuration)
    {
        return configuration.DependencyRegistry
            .Where(descriptor =>
                descriptor.DependencyType == typeof(ICompositionSectionContributor) &&
                descriptor.IsCollectionRegistration)
            .Select(descriptor => (ICompositionSectionContributor) descriptor.Instance!)
            .ToList();
    }

    private sealed class ChainTestDocument : IAuditChainDocument
    {
        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }

        public string? Action { get; set; }

        public string? Category { get; set; }
    }

    private sealed class OtherChainTestDocument : IAuditChainDocument
    {
        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }
    }

    private sealed class ChainTestMapper : AuditRecordMapper<ChainTestDocument>
    {
        public override ChainTestDocument Map(AuditRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            return new ChainTestDocument { Action = record.Action, Category = record.Category };
        }
    }

    private sealed class ChainTestSegmenter : IAuditSegmenter<ChainTestDocument>
    {
        public string SegmentOf(ChainTestDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            return "test-" + (document.Category ?? "segment");
        }

        public ChainTestDocument? CreateGenesis(string segment, ChainTestDocument first)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segment);
            ArgumentNullException.ThrowIfNull(first);

            return null;
        }
    }

    private sealed class NotASegmenter
    {
    }

    private sealed class FakeModuleConfiguration : IModuleConfiguration
    {
        private readonly List<Type> _startupTasks = [];
        private readonly List<Type> _backgroundServices = [];
        private readonly List<DiagnosticCheckDescriptor> _diagnosticChecks = [];
        private readonly List<Action> _compositionValidations = [];
        private readonly Dictionary<Type, object> _contexts = [];

        public FakeModuleConfiguration()
        {
            DependencyRegistry = new DependencyRegistry();
            SetContext<IMessageRegistry>(MessageRegistryFactory.Create());
        }

        public IDependencyRegistry DependencyRegistry { get; }

        public IReadOnlyList<Type> StartupTasks => _startupTasks;

        public IReadOnlyList<Type> BackgroundServices => _backgroundServices;

        public IReadOnlyList<DiagnosticCheckDescriptor> DiagnosticChecks => _diagnosticChecks;

        public IReadOnlyList<Action> CompositionValidations => _compositionValidations;

        public void RegisterStartupTask(Type implementationType)
        {
            ArgumentNullException.ThrowIfNull(implementationType);

            if (!typeof(IStartupTask).IsAssignableFrom(implementationType))
            {
                throw new ArgumentException(
                    $"Type '{implementationType.FullName}' must implement {nameof(IStartupTask)}.",
                    nameof(implementationType));
            }

            if (!_startupTasks.Contains(implementationType))
            {
                _startupTasks.Add(implementationType);
            }
        }

        public void RegisterBackgroundService(Type implementationType)
        {
            ArgumentNullException.ThrowIfNull(implementationType);

            if (!typeof(IBackgroundService).IsAssignableFrom(implementationType))
            {
                throw new ArgumentException(
                    $"Type '{implementationType.FullName}' must implement {nameof(IBackgroundService)}.",
                    nameof(implementationType));
            }

            if (!implementationType.IsClass || implementationType.IsAbstract)
            {
                throw new ArgumentException(
                    $"Type '{implementationType.FullName}' must be a concrete class.",
                    nameof(implementationType));
            }

            if (!_backgroundServices.Contains(implementationType))
            {
                _backgroundServices.Add(implementationType);
            }
        }

        public void RegisterDiagnosticCheck(Type implementationType, string name)
        {
            ArgumentNullException.ThrowIfNull(implementationType);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            _diagnosticChecks.Add(new DiagnosticCheckDescriptor(implementationType, name));
        }

        public void RegisterCompositionValidation(Action validate)
        {
            ArgumentNullException.ThrowIfNull(validate);

            _compositionValidations.Add(validate);
        }

        public T GetContext<T>()
            where T : class
        {
            if (_contexts.TryGetValue(typeof(T), out var context))
            {
                return (T) context;
            }

            throw new LiteBusConfigurationException($"Context of type '{typeof(T).Name}' was not found.");
        }

        public void SetContext<T>(T context)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(context);

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
            ArgumentNullException.ThrowIfNull(factory);

            if (_contexts.TryGetValue(typeof(T), out var stored))
            {
                return (T) stored;
            }

            var created = factory();

            _contexts[typeof(T)] = created;

            return created;
        }
    }
}
