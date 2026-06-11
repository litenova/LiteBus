using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the Entity Framework Core outbox store with LiteBus dependency injection.
/// </summary>
public sealed class EfCoreOutboxStorageModule : IOutboxStorageModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<EfCoreOutboxStorageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStorageModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public EfCoreOutboxStorageModule(Action<EfCoreOutboxStorageModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new EfCoreOutboxStorageModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DbContextType is null)
        {
            throw new LiteBusConfigurationException(
                "An outbox database context must be configured. Call UseDbContext<TContext>() on the EF Core outbox storage builder.");
        }

        if (moduleBuilder.RequireTransactionalSetup && !moduleBuilder.RegisterSaveChangesInterceptor)
        {
            throw new LiteBusConfigurationException(
                "EnforceTransactionalSetup() is enabled but EnableSaveChangesInterceptor() was not called. " +
                "Call EnableSaveChangesInterceptor() on the EF Core outbox storage builder and add " +
                "optionsBuilder.AddLiteBusOutboxInterceptor(interceptor) to your DbContext configuration. " +
                "See docs/Outbox.md for the complete transactional setup.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EfCoreOutboxStoreOptions),
            moduleBuilder.Options));

        if (moduleBuilder.RegisterSaveChangesInterceptor)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(LiteBusOutboxSaveChangesInterceptor),
                _ => new LiteBusOutboxSaveChangesInterceptor(),
                InstanceLifetime.Singleton));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EfCoreOutboxStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxLeaseStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStateWriter),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDeadLetterStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxRetentionStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDiagnosticsStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxMessageQuery),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxPurgeStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessingStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxOperationsStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransactionalOutboxStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreOutboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxWorkSignal),
            typeof(OutboxPollingWorkSignal)));

        if (moduleBuilder.RegisterSaveChangesInterceptor)
        {
            var transactionalOutboxType = typeof(ITransactionalOutbox<>).MakeGenericType(moduleBuilder.DbContextType);

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                transactionalOutboxType,
                serviceProvider => CreateTransactionalOutbox(serviceProvider, moduleBuilder),
                InstanceLifetime.Scoped));
        }
    }

    /// <summary>
    ///     Creates a transactional outbox bound to the configured application database context.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="moduleBuilder">The configured module builder.</param>
    /// <returns>The transactional outbox instance.</returns>
    private static object CreateTransactionalOutbox(
        IServiceProvider serviceProvider,
        EfCoreOutboxStorageModuleBuilder moduleBuilder)
    {
        var dbContext = serviceProvider.GetRequiredService(moduleBuilder.DbContextType!);
        var transactionalOutboxType = typeof(TransactionalOutbox<>).MakeGenericType(moduleBuilder.DbContextType!);

        return Activator.CreateInstance(
            transactionalOutboxType,
            serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>(),
            dbContext,
            serviceProvider.GetRequiredService<IOutboxEnvelopeFactory>(),
            serviceProvider.GetRequiredService<TimeProvider>())!;
    }

    /// <summary>
    ///     Creates the outbox store from the configured database context type.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="moduleBuilder">The configured module builder.</param>
    /// <returns>The outbox store instance.</returns>
    private static EfCoreOutboxStore CreateStore(
        IServiceProvider serviceProvider,
        EfCoreOutboxStorageModuleBuilder moduleBuilder)
    {
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new EfCoreOutboxStore(scopeFactory, moduleBuilder.DbContextType!, moduleBuilder.Options);
    }
}