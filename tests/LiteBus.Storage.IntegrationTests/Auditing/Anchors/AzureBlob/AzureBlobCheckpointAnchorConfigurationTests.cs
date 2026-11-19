using System.Collections;
using LiteBus.Auditing.Abstractions;
using Azure.Storage.Blobs;
using LiteBus.Auditing;
using LiteBus.Auditing.Anchors.AzureBlob;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Storage.IntegrationTests.Auditing.Anchors.AzureBlob;

/// <summary>
///     Tests for Azure Blob anchor configuration: builder registration, module composition, settings validation, and checkpoint text.
/// </summary>
public sealed class AzureBlobCheckpointAnchorConfigurationTests
{
    [Fact]
    public void PublishCheckpointsToAzureBlob_records_publisher_source_types_and_child_module()
    {
        var builder = new AuditingModuleBuilder();

        builder.UseHashChain(chain => chain.PublishCheckpointsToAzureBlob(anchor =>
        {
            anchor.ConnectionString = "UseDevelopmentStorage=true";
            anchor.ContainerName = "checkpoints";
        }));

        var chain = builder.Options.Chain;
        chain.Should().NotBeNull();
        chain!.PublisherTypes.Should().Contain(typeof(AzureBlobCheckpointPublisher));
        chain.SourceTypes.Should().Contain(typeof(AzureBlobCheckpointSource));
        builder.CollectSubModules().Should().ContainSingle().Subject.Should().BeOfType<AzureBlobCheckpointAnchorModule>();
    }

    [Fact]
    public void PublishCheckpointsToAzureBlob_twice_registers_the_anchor_once()
    {
        var builder = new AuditingModuleBuilder();

        builder.UseHashChain(chain => chain
            .PublishCheckpointsToAzureBlob(anchor =>
            {
                anchor.ConnectionString = "UseDevelopmentStorage=true";
                anchor.ContainerName = "checkpoints";
            })
            .PublishCheckpointsToAzureBlob(anchor =>
            {
                anchor.ConnectionString = "UseDevelopmentStorage=true";
                anchor.ContainerName = "checkpoints";
            }));

        builder.CollectSubModules().Should().ContainSingle();
        builder.Options.Chain!.PublisherTypes.Should().ContainSingle();
        builder.Options.Chain.SourceTypes.Should().ContainSingle();
    }

    [Fact]
    public void PublishCheckpointsToAzureBlob_rejects_invalid_options()
    {
        var builder = new AuditingModuleBuilder();

        var configure = () => builder.UseHashChain(chain => chain
            .PublishCheckpointsToAzureBlob(anchor =>
            {
                anchor.ConnectionString = "UseDevelopmentStorage=true";
                anchor.ContainerName = "Invalid_Name";
            }));

        configure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PublishCheckpointsToAzureBlob_rejects_a_null_builder()
    {
        var configure = () => ((AuditHashChainBuilder) null!).PublishCheckpointsToAzureBlob(_ => { });

        configure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PublishCheckpointsToAzureBlob_rejects_a_null_configure()
    {
        var builder = new AuditingModuleBuilder();

        var configure = () => builder.UseHashChain(chain => chain.PublishCheckpointsToAzureBlob(null!));

        configure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PublishCheckpointsToAzureBlob_declares_the_anchor_module_as_a_child()
    {
        var module = new AuditingModule(auditing => auditing
            .UseHashChain(chain => chain.PublishCheckpointsToAzureBlob(anchor =>
            {
                anchor.ConnectionString = "UseDevelopmentStorage=true";
                anchor.ContainerName = "checkpoints";
            })));

        var registry = new TestModuleRegistry();
        module.DeclareChildren(child => registry.Register(child));

        registry.Find<AzureBlobCheckpointAnchorModule>().Should().NotBeNull();
    }

    [Fact]
    public void Module_Build_registers_settings_client_and_probe()
    {
        var configuration = new TestModuleConfiguration();

        new AzureBlobCheckpointAnchorModule(ValidOptions()).Build(configuration);

        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(AzureBlobCheckpointAnchorOptions));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(BlobServiceClient));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(AzureBlobCheckpointAnchorDiagnosticCheck));
        configuration.DiagnosticChecks.Should().ContainSingle().Subject.Name.Should().Be(AzureBlobCheckpointAnchorDiagnosticCheck.ProbeName);
    }

    [Fact]
    public void Module_rejects_invalid_options()
    {
        var create = () => new AzureBlobCheckpointAnchorModule(ValidOptions() with { ConnectionString = "  " });

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("checkpoints")]
    [InlineData("checkpoints-8f3a2b1c4d5e6f708192a3b4c5d6e7f8")]
    [InlineData("abc")]
    [InlineData("a1-b2-c3")]
    public void Module_accepts_valid_container_names(string containerName)
    {
        var create = () => new AzureBlobCheckpointAnchorModule(ValidOptions() with { ContainerName = containerName });

        create.Should().NotThrow();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("ABC")]
    [InlineData("-checkpoints")]
    [InlineData("checkpoints-")]
    [InlineData("check--points")]
    [InlineData("check_points")]
    [InlineData("checkpoints/checkpoints")]
    public void Module_rejects_invalid_container_names(string containerName)
    {
        var create = () => new AzureBlobCheckpointAnchorModule(ValidOptions() with { ContainerName = containerName });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_container_name_above_the_length_limit()
    {
        var create = () => new AzureBlobCheckpointAnchorModule(ValidOptions() with { ContainerName = new string('a', 64) });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_relative_prefix_violation()
    {
        var create = () => new AzureBlobCheckpointAnchorModule(ValidOptions() with { BlobNamePrefix = "/audit" });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_malformed_connection_string()
    {
        var create = () => new AzureBlobCheckpointAnchorModule(ValidOptions() with { ConnectionString = "not-a-connection-string" });

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Module_rejects_a_non_positive_immutability_retention(int days)
    {
        var options = ValidOptions() with { ImmutabilityRetention = TimeSpan.FromDays(days) };

        var create = () => new AzureBlobCheckpointAnchorModule(options);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Checkpoint_codec_round_trips_a_checkpoint()
    {
        var original = new AuditCheckpoint(
            Guid.NewGuid(),
            "tenant/2026",
            7,
            7,
            "hash-7",
            "sha256-v2",
            AuditCheckpointKind.Final,
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
        {
            Label = "daily",
            Signature = new AuditCheckpointSignature("test", "sig-7"),
            Signatures =
            [
                new AuditCheckpointSignature("test", "sig-7"),
                new AuditCheckpointSignature("witness", "wit-7"),
            ],
        };

        var text = AzureBlobCheckpointCodec.Serialize(original);
        var restored = AzureBlobCheckpointCodec.Deserialize("tenant%2F2026/00000000000000000007.json", text);

        restored.Id.Should().Be(original.Id);
        restored.Segment.Should().Be(original.Segment);
        restored.Sequence.Should().Be(original.Sequence);
        restored.Count.Should().Be(original.Count);
        restored.Hash.Should().Be(original.Hash);
        restored.HashScheme.Should().Be(original.HashScheme);
        restored.Kind.Should().Be(original.Kind);
        restored.CreatedAt.Should().Be(original.CreatedAt);
        restored.Label.Should().Be(original.Label);
        restored.Signature.Should().Be(original.Signature);
        restored.Signatures.Should().Equal(original.Signatures);
    }

    [Fact]
    public void Checkpoint_codec_rejects_text_without_a_checkpoint()
    {
        var parse = () => AzureBlobCheckpointCodec.Deserialize("tenant%2F2026/blob.json", "{ not json");

        parse.Should().Throw<AzureBlobCheckpointAnchorException>();
    }

    [Fact]
    public void Blob_names_encode_nested_segments()
    {
        var checkpointId = Guid.NewGuid();

        AzureBlobCheckpointBlobNames.GetSegmentPrefix("", "tenant/2026").Should().Be("tenant%2F2026/");
        AzureBlobCheckpointBlobNames.GetBlobName("audit", "tenant/2026", 3, checkpointId)
            .Should().Be($"audit/tenant%2F2026/{3:D20}-{checkpointId:N}.json");
        AzureBlobCheckpointBlobNames.IsCheckpointBlob("tenant%2F2026/00000000000000000003.json").Should().BeTrue();
        AzureBlobCheckpointBlobNames.IsCheckpointBlob("readme.txt").Should().BeFalse();
    }

    private static AzureBlobCheckpointAnchorOptions ValidOptions()
    {
        return new AzureBlobCheckpointAnchorOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "checkpoints",
        };
    }

    private sealed class TestModuleRegistry : IModuleRegistry
    {
        private readonly List<IModule> _modules = [];

        public IModuleRegistry Register(IModule module)
        {
            _modules.Add(module);
            return this;
        }

        public bool IsModuleRegistered<T>()
            where T : IModule
        {
            return _modules.OfType<T>().Any();
        }

        public T? Find<T>()
            where T : class, IModule
        {
            return _modules.OfType<T>().FirstOrDefault();
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
