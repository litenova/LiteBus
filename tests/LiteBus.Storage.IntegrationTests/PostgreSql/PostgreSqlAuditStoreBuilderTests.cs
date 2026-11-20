using System.Collections;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Tests for the PostgreSQL audit store registration: the recorded trail, chain store, capabilities, defaults,
///     and module composition.
/// </summary>
public sealed class PostgreSqlAuditStoreBuilderTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditStoreBuilderTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlAuditStoreBuilderTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public void UsePostgreSqlStore_registers_the_postgresql_trail_chain_store_and_capabilities()
    {
        var builder = new AuditingModuleBuilder()
            .UsePostgreSqlStore(postgres => postgres.UseDataSource(_fixture.DataSource));
        var options = builder.Options;

        options.TrailType.Should().Be(typeof(PostgreSqlAuditTrail<>));
        options.ChainStoreType.Should().Be(typeof(PostgreSqlAuditChainStore<>));
        options.StoreCapabilities.Should().NotBeNull();
        options.StoreCapabilities!.JoinsBusinessTransaction.Should().BeTrue();
    }

    [SkippableFact]
    public void UsePostgreSqlStore_records_the_postgresql_document_mapper_and_segmenter_defaults()
    {
        var options = new AuditingModuleBuilder()
            .UsePostgreSqlStore(postgres => postgres.UseDataSource(_fixture.DataSource)).Options;

        options.StoreDefaultDocumentType.Should().Be<PostgreSqlAuditRecord>();
        options.StoreDefaultMapperType.Should().Be<PostgreSqlAuditRecordMapper>();
        options.StoreDefaultSegmenterType.Should().Be<PostgreSqlAuditRecordSegmenter>();
    }

    [SkippableFact]
    public void UsePostgreSqlStore_registers_the_postgresql_module_as_a_child()
    {
        var builder = new AuditingModuleBuilder()
            .UsePostgreSqlStore(postgres => postgres.UseDataSource(_fixture.DataSource));

        builder.CollectSubModules().Should().ContainSingle().Subject.Should().BeOfType<PostgreSqlAuditModule>();
    }

    [SkippableFact]
    public void UsePostgreSqlStore_twice_is_a_noop()
    {
        var builder = new AuditingModuleBuilder()
            .UsePostgreSqlStore(postgres => postgres.UseDataSource(_fixture.DataSource));

        var repeat = () => builder.UsePostgreSqlStore(postgres => postgres.UseDataSource(_fixture.DataSource));

        repeat.Should().NotThrow();
        builder.Options.TrailType.Should().Be(typeof(PostgreSqlAuditTrail<>));
        builder.Options.ChainStoreType.Should().Be(typeof(PostgreSqlAuditChainStore<>));
        builder.CollectSubModules().Should().ContainSingle();
    }

    [SkippableFact]
    public void UsePostgreSqlStore_after_another_store_throws_and_names_both()
    {
        var builder = new AuditingModuleBuilder();
        builder.Options.TrailType = typeof(object);
        builder.Options.ChainStoreType = typeof(string);

        var use = () => builder.UsePostgreSqlStore(postgres => postgres.UseDataSource(_fixture.DataSource));

        var thrown = use.Should().Throw<LiteBusConfigurationException>();
        thrown.WithMessage("*System.Object*System.String*PostgreSQL*");
        builder.CollectSubModules().Should().BeEmpty();
    }

    [SkippableFact]
    public void UsePostgreSqlStore_rejects_a_null_builder()
    {
        var use = () => ((AuditingModuleBuilder) null!).UsePostgreSqlStore(_ => { });

        use.Should().Throw<ArgumentNullException>();
    }

    [SkippableFact]
    public void UsePostgreSqlStore_rejects_a_null_configure()
    {
        var builder = new AuditingModuleBuilder();

        var use = () => builder.UsePostgreSqlStore(null!);

        use.Should().Throw<ArgumentNullException>();
    }

    [SkippableFact]
    public void Module_rejects_a_null_builder_action()
    {
        var create = () => new PostgreSqlAuditModule(null!);

        create.Should().Throw<ArgumentNullException>();
    }

    [SkippableFact]
    public void Build_without_a_data_source_throws()
    {
        var configuration = new TestModuleConfiguration();

        var build = () => new PostgreSqlAuditModule(_ => { }).Build(configuration);

        build.Should().Throw<DurableStorageConfigurationException>().WithMessage("*data source*");
    }

    [SkippableFact]
    public void Build_registers_the_data_source_owner_registration_probe_and_bootstrap()
    {
        var configuration = new TestModuleConfiguration();

        new PostgreSqlAuditModule(postgres => postgres.UseDataSource(_fixture.DataSource)).Build(configuration);

        configuration.DependencyRegistry.Should().Contain(
            descriptor => descriptor.DependencyType == typeof(PostgreSqlDataSourceOwner<PostgreSqlAuditModule>));
        configuration.DependencyRegistry.Should().Contain(
            descriptor => descriptor.DependencyType == typeof(PostgreSqlAuditStoreRegistration));
        configuration.DependencyRegistry.Should().Contain(
            descriptor => descriptor.DependencyType == typeof(PostgreSqlAuditSchemaInitializer));
        configuration.StartupTasks.Should().Contain(typeof(PostgreSqlAuditSchemaInitializer));
        configuration.DiagnosticChecks.Should().Contain(
            check => check.ImplementationType == typeof(PostgreSqlAuditSchemaDiagnosticCheck)
                && check.Name == PostgreSqlAuditSchemaDiagnosticCheck.CheckName);
    }

    [SkippableFact]
    public void Build_with_schema_initialization_disabled_skips_the_startup_task_but_keeps_the_probe()
    {
        var configuration = new TestModuleConfiguration();

        new PostgreSqlAuditModule(postgres => postgres
            .UseDataSource(_fixture.DataSource)
            .DisableSchemaInitialization()).Build(configuration);

        configuration.StartupTasks.Should().BeEmpty();
        configuration.DiagnosticChecks.Should().Contain(
            check => check.ImplementationType == typeof(PostgreSqlAuditSchemaDiagnosticCheck));
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
