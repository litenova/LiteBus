using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL audit store.
/// </summary>
/// <remarks>
///     <para>
///         Declared with a configuration action, following the recurring lease module: the action runs when the
///         module builds, so the data source and the table options are read then and not at registration time.
///         <c>AuditingModuleBuilder.UsePostgreSqlStore</c> registers this module as a child of the auditing
///         module. The module registers the Npgsql data source, the table options, the default document
///         canonicalizer, the schema bootstrap, and the schema probe. The chain store and the trail themselves
///         are registered by <see cref="AuditingModule" />, which closes the open-generic types that
///         <c>AuditingModuleBuilder.UsePostgreSqlStore</c> recorded over the configured document type.
///     </para>
///     <para>
///         The default canonicalizer for <see cref="PostgreSqlAuditRecord" /> is registered here because the
///         chain engine resolves the canonicalizer per document type from the container. An application with
///         its own document registers its own; the closed registration below never collides with it because
///         each closed generic type is a distinct service. The default mapper and segmenter are not registered
///         here: the auditing module registers them from the recorded defaults, and a second binding for the
///         same service would fail composition.
///     </para>
/// </remarks>
public sealed class PostgreSqlAuditModule : IModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<PostgreSqlAuditModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    public PostgreSqlAuditModule(Action<PostgreSqlAuditModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlAuditModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new DurableStorageConfigurationException(
                "A PostgreSQL audit data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        moduleBuilder.Options.Validate();

        var dataSourceOwner = new PostgreSqlDataSourceOwner<PostgreSqlAuditModule>(
            moduleBuilder.DataSource,
            moduleBuilder.OwnsDataSource);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlDataSourceOwner<PostgreSqlAuditModule>),
            _ => dataSourceOwner,
            InstanceLifetime.Singleton));

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(NpgsqlDataSource), moduleBuilder.DataSource));
        }

        var registration = new PostgreSqlAuditStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlAuditStoreRegistration),
            services =>
            {
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlAuditModule>));
                return registration;
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditCanonicalizer<PostgreSqlAuditRecord>),
            new PostgreSqlAuditRecordCanonicalizer()));

        if (moduleBuilder.IsSchemaInitializationEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlAuditSchemaInitializer),
                typeof(PostgreSqlAuditSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlAuditSchemaInitializer));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlAuditSchemaDiagnosticCheck),
            typeof(PostgreSqlAuditSchemaDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(PostgreSqlAuditSchemaDiagnosticCheck),
            PostgreSqlAuditSchemaDiagnosticCheck.CheckName);
    }

    /// <summary>
    ///     Creates a singleton registration that resolves the module's data source owner before returning
    ///     <paramref name="instance" />.
    /// </summary>
    /// <param name="dependencyType">The service type to register.</param>
    /// <param name="instance">The prebuilt service the registration returns.</param>
    /// <returns>A singleton factory registration.</returns>
    /// <remarks>
    ///     Resolving the owner from every store registration makes the container create and capture it the first time
    ///     any of the module's services is used, so disposing the container disposes a data source the module created.
    ///     A prebuilt instance registration would not: Microsoft dependency injection never disposes one.
    /// </remarks>
    private static DependencyDescriptor CreateOwnedRegistration(Type dependencyType, object instance)
    {
        return new DependencyDescriptor(
            dependencyType,
            services =>
            {
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlAuditModule>));
                return instance;
            },
            InstanceLifetime.Singleton);
    }
}
