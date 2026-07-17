using System;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Extensions;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the Entity Framework Core inbox store with LiteBus dependency injection.
/// </summary>
public sealed class EfCoreInboxStorageModule : IInboxStorageModule, IRequires<InboxModule>
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<EfCoreInboxStorageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStorageModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public EfCoreInboxStorageModule(Action<EfCoreInboxStorageModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new EfCoreInboxStorageModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DbContextType is null)
        {
            throw new LiteBusConfigurationException(
                "An inbox database context must be configured. Call UseDbContext<TContext>() on the EF Core inbox storage builder.");
        }

        if (moduleBuilder is { RequireTransactionalSetup: true, RegisterSaveChangesInterceptor: false })
        {
            throw new LiteBusConfigurationException(
                "EnforceTransactionalSetup() is enabled but EnableSaveChangesInterceptor() was not called. " +
                "Call EnableSaveChangesInterceptor() on the EF Core inbox storage builder and add " +
                "optionsBuilder.AddLiteBusInboxInterceptor(interceptor) to your DbContext configuration. " +
                "See docs/reliable-messaging/inbox.md for the complete transactional setup.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EntityFrameworkCoreInboxStoreOptions),
            moduleBuilder.Options));

        if (moduleBuilder.RegisterSaveChangesInterceptor)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(LiteBusInboxSaveChangesInterceptor),
                _ => new LiteBusInboxSaveChangesInterceptor(),
                InstanceLifetime.Singleton));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IEfCoreInboxDbContextFactory),
            serviceProvider => CreateDbContextFactory(serviceProvider, moduleBuilder.DbContextType),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EfCoreInboxStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxLeaseStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxStateWriter),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDeadLetterStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxRetentionStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDiagnosticsStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxMessageQuery),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxPurgeStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxProcessingStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxOperationsStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransactionalInboxStore),
            serviceProvider => serviceProvider.GetRequiredService<EfCoreInboxStore>(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxWorkSignal),
            typeof(InboxPollingWorkSignal)));

        if (moduleBuilder.RegisterSaveChangesInterceptor)
        {
            var transactionalInboxType = typeof(ITransactionalInbox<>).MakeGenericType(moduleBuilder.DbContextType);

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                transactionalInboxType,
                serviceProvider => CreateTransactionalInbox(serviceProvider, moduleBuilder),
                InstanceLifetime.Scoped));
        }
    }

    /// <summary>
    ///     Creates a transactional inbox bound to the configured application database context.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="moduleBuilder">The configured module builder.</param>
    /// <returns>The transactional inbox instance.</returns>
    private static object CreateTransactionalInbox(
        IServiceProvider serviceProvider,
        EfCoreInboxStorageModuleBuilder moduleBuilder)
    {
        var dbContext = serviceProvider.GetRequiredService(moduleBuilder.DbContextType!);
        var transactionalInboxType = typeof(TransactionalInbox<>).MakeGenericType(moduleBuilder.DbContextType!);

        return Activator.CreateInstance(
            transactionalInboxType,
            serviceProvider.GetRequiredService<LiteBusInboxSaveChangesInterceptor>(),
            dbContext,
            serviceProvider.GetRequiredService<IInboxEnvelopeFactory>())!;
    }

    /// <summary>
    ///     Creates the inbox store from the configured database context type.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="moduleBuilder">The configured module builder.</param>
    /// <returns>The inbox store instance.</returns>
    private static EfCoreInboxStore CreateStore(
        IServiceProvider serviceProvider,
        EfCoreInboxStorageModuleBuilder moduleBuilder)
    {
        return new EfCoreInboxStore(
            serviceProvider.GetRequiredService<IEfCoreInboxDbContextFactory>(),
            moduleBuilder.Options);
    }

    /// <summary>
    ///     Creates the adapter that owns EF Core contexts for inbox store operations.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="dbContextType">The configured application database context type.</param>
    /// <returns>The context factory adapter.</returns>
    private static object CreateDbContextFactory(IServiceProvider serviceProvider, Type? dbContextType)
    {
        var contextType = dbContextType ?? throw new LiteBusConfigurationException(
            "An inbox database context must be configured before the context factory is created.");
        var factoryContract = typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<>).MakeGenericType(contextType);
        var factory = serviceProvider.GetRequiredService(factoryContract);
        var adapterType = typeof(EfCoreInboxDbContextFactory<>).MakeGenericType(contextType);

        return Activator.CreateInstance(adapterType, factory)!;
    }
}
