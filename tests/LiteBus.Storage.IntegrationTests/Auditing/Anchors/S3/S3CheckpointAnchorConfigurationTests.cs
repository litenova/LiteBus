using System.Collections;
using LiteBus.Auditing.Abstractions;
using System.Text;
using Amazon.S3;
using LiteBus.Auditing;
using LiteBus.Auditing.Anchors.S3;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Storage.IntegrationTests.Auditing.Anchors.S3;

/// <summary>
///     Tests for S3 anchor configuration: builder registration, module composition, settings validation, and checkpoint text.
/// </summary>
public sealed class S3CheckpointAnchorConfigurationTests
{
    [Fact]
    public void PublishCheckpointsToS3_records_publisher_source_types_and_child_module()
    {
        var builder = new AuditingModuleBuilder();

        builder.UseHashChain(chain => chain.PublishCheckpointsToS3(anchor => anchor.BucketName = "checkpoints"));

        var chain = builder.Options.Chain;
        chain.Should().NotBeNull();
        chain!.PublisherTypes.Should().Contain(typeof(S3CheckpointPublisher));
        chain.SourceTypes.Should().Contain(typeof(S3CheckpointSource));
        builder.CollectSubModules().Should().ContainSingle().Subject.Should().BeOfType<S3CheckpointAnchorModule>();
    }

    [Fact]
    public void PublishCheckpointsToS3_twice_registers_the_anchor_once()
    {
        var builder = new AuditingModuleBuilder();

        builder.UseHashChain(chain => chain
            .PublishCheckpointsToS3(anchor => anchor.BucketName = "checkpoints")
            .PublishCheckpointsToS3(anchor => anchor.BucketName = "checkpoints"));

        builder.CollectSubModules().Should().ContainSingle();
        builder.Options.Chain!.PublisherTypes.Should().ContainSingle();
        builder.Options.Chain.SourceTypes.Should().ContainSingle();
    }

    [Fact]
    public void PublishCheckpointsToS3_rejects_invalid_options()
    {
        var builder = new AuditingModuleBuilder();

        var configure = () => builder.UseHashChain(chain => chain
            .PublishCheckpointsToS3(anchor => anchor.BucketName = "Invalid_Name"));

        configure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PublishCheckpointsToS3_rejects_a_null_builder()
    {
        var configure = () => ((AuditHashChainBuilder) null!).PublishCheckpointsToS3(_ => { });

        configure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PublishCheckpointsToS3_rejects_a_null_configure()
    {
        var builder = new AuditingModuleBuilder();

        var configure = () => builder.UseHashChain(chain => chain.PublishCheckpointsToS3(null!));

        configure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PublishCheckpointsToS3_declares_the_anchor_module_as_a_child()
    {
        var module = new AuditingModule(auditing => auditing
            .UseHashChain(chain => chain.PublishCheckpointsToS3(anchor => anchor.BucketName = "checkpoints")));

        var registry = new TestModuleRegistry();
        module.DeclareChildren(child => registry.Register(child));

        registry.Find<S3CheckpointAnchorModule>().Should().NotBeNull();
    }

    [Fact]
    public void Module_Build_registers_settings_client_and_probe()
    {
        var configuration = new TestModuleConfiguration();

        new S3CheckpointAnchorModule(ValidOptions()).Build(configuration);

        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(S3CheckpointAnchorOptions));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(IAmazonS3));
        configuration.DependencyRegistry.Should().Contain(descriptor => descriptor.DependencyType == typeof(S3CheckpointAnchorDiagnosticCheck));
        configuration.DiagnosticChecks.Should().ContainSingle().Subject.Name.Should().Be(S3CheckpointAnchorDiagnosticCheck.ProbeName);
    }

    [Fact]
    public void Probe_name_is_stable()
    {
        S3CheckpointAnchorDiagnosticCheck.ProbeName.Should().Be("auditing.anchor.s3.connectivity");
    }

    [Fact]
    public void Module_rejects_invalid_options()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { BucketName = "  " });

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("checkpoints")]
    [InlineData("checkpoints-8f3a2b1c4d5e6f708192a3b4c5d6e7f8")]
    [InlineData("abc")]
    [InlineData("a1-b2-c3")]
    [InlineData("with.dot")]
    [InlineData("dots.and-dashes.1")]
    public void Module_accepts_valid_bucket_names(string bucketName)
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { BucketName = bucketName });

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
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("double..dots")]
    public void Module_rejects_invalid_bucket_names(string bucketName)
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { BucketName = bucketName });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_accepts_a_bucket_name_at_the_length_limit()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { BucketName = new string('a', 63) });

        create.Should().NotThrow();
    }

    [Fact]
    public void Module_rejects_a_bucket_name_above_the_length_limit()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { BucketName = new string('a', 64) });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_relative_prefix_violation()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { KeyPrefix = "/audit" });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_blank_prefix()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { KeyPrefix = "   " });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_an_access_key_without_a_secret_key()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { AccessKey = "access" });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_secret_key_without_an_access_key()
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { SecretKey = "secret" });

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://files.example")]
    public void Module_rejects_a_malformed_service_url(string serviceUrl)
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { ServiceUrl = serviceUrl });

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("http://localhost:9000")]
    [InlineData("https://s3.example")]
    public void Module_accepts_http_service_urls(string serviceUrl)
    {
        var create = () => new S3CheckpointAnchorModule(ValidOptions() with { ServiceUrl = serviceUrl });

        create.Should().NotThrow();
    }

    [Fact]
    public void Module_rejects_a_retention_mode_without_a_period()
    {
        var options = ValidOptions() with { RetentionMode = S3CheckpointRetentionMode.Governance };

        var create = () => new S3CheckpointAnchorModule(options);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Module_rejects_a_retention_period_without_a_mode()
    {
        var options = ValidOptions() with { RetentionPeriod = TimeSpan.FromDays(30) };

        var create = () => new S3CheckpointAnchorModule(options);

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Module_rejects_a_non_positive_retention_period(int days)
    {
        var options = ValidOptions() with
        {
            RetentionMode = S3CheckpointRetentionMode.Governance,
            RetentionPeriod = TimeSpan.FromDays(days),
        };

        var create = () => new S3CheckpointAnchorModule(options);

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
            Signature = TestSignature("test", "signature-bytes-for-test-signer"),
            Signatures =
            [
                TestSignature("test", "signature-bytes-for-test-signer"),
                TestSignature("witness", "signature-bytes-for-witness"),
            ],
        };

        var text = S3CheckpointNoteCodec.Serialize("tenant%2F2026/00000000000000000007.checkpoint", original);
        var restored = S3CheckpointNoteCodec.Deserialize("tenant%2F2026/00000000000000000007.checkpoint", text);

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
    public void Checkpoint_codec_renders_transparency_log_checkpoint_text()
    {
        var checkpoint = new AuditCheckpoint(
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
            Signatures = [TestSignature("test", "signature-bytes-for-test-signer")],
        };

        var text = S3CheckpointNoteCodec.Serialize("tenant%2F2026/00000000000000000007.checkpoint", checkpoint);

        text.Should().StartWith("tenant/2026\n7\n");
        text.Should().Contain("sequence: 7");
        text.Should().Contain("scheme: sha256-v2");
        text.Should().Contain("kind: Final");
        text.Should().Contain("label: daily");
        text.Should().Contain("— test ");
    }

    [Fact]
    public void Checkpoint_codec_round_trips_an_unsigned_checkpoint()
    {
        var original = new AuditCheckpoint(
            Guid.NewGuid(),
            "ledger",
            1,
            1,
            "hash-1",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));

        var text = S3CheckpointNoteCodec.Serialize("ledger/00000000000000000001.checkpoint", original);
        var restored = S3CheckpointNoteCodec.Deserialize("ledger/00000000000000000001.checkpoint", text);

        restored.Id.Should().Be(original.Id);
        restored.Signature.Should().BeNull();
        restored.Signatures.Should().BeEmpty();
    }

    [Fact]
    public void Checkpoint_codec_rejects_text_without_a_checkpoint()
    {
        var parse = () => S3CheckpointNoteCodec.Deserialize("tenant%2F2026/blob.checkpoint", "not a checkpoint");

        parse.Should().Throw<S3CheckpointAnchorException>();
    }

    [Fact]
    public void Checkpoint_codec_rejects_a_signature_that_is_not_base64()
    {
        var checkpoint = new AuditCheckpoint(
            Guid.NewGuid(),
            "ledger",
            1,
            1,
            "hash-1",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
        {
            Signatures = [new AuditCheckpointSignature("test", "not-base64!")],
        };

        var serialize = () => S3CheckpointNoteCodec.Serialize("ledger/00000000000000000001.checkpoint", checkpoint);

        serialize.Should().Throw<S3CheckpointAnchorException>();
    }

    [Fact]
    public void Object_keys_encode_nested_segments()
    {
        var checkpointId = Guid.NewGuid();

        S3CheckpointAnchorKeys.GetSegmentPrefix("", "tenant/2026").Should().Be("tenant%2F2026/");
        S3CheckpointAnchorKeys.GetObjectKey("audit", "tenant/2026", 3, checkpointId)
            .Should().Be($"audit/tenant%2F2026/{3:D20}-{checkpointId:N}.checkpoint");
        S3CheckpointAnchorKeys.IsCheckpointObject("tenant%2F2026/00000000000000000003.checkpoint").Should().BeTrue();
        S3CheckpointAnchorKeys.IsCheckpointObject("readme.txt").Should().BeFalse();
    }

    private static S3CheckpointAnchorOptions ValidOptions()
    {
        return new S3CheckpointAnchorOptions
        {
            BucketName = "checkpoints",
        };
    }

    private static AuditCheckpointSignature TestSignature(string scheme, string text)
    {
        return new AuditCheckpointSignature(scheme, Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
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
