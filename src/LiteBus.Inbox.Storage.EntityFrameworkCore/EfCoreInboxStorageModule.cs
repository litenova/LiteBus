using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the Entity Framework Core inbox store with LiteBus dependency injection.
/// </summary>
public sealed class EfCoreInboxStorageModule : IInboxStorageModule
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
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
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

        if (moduleBuilder.RequireTransactionalSetup && !moduleBuilder.RegisterSaveChangesInterceptor)
        {
            throw new LiteBusConfigurationException(
                "EnforceTransactionalSetup() is enabled but EnableSaveChangesInterceptor() was not called. " +
                "Call EnableSaveChangesInterceptor() on the EF Core inbox storage builder and add " +
                "optionsBuilder.AddLiteBusInboxInterceptor(interceptor) to your DbContext configuration. " +
                "See docs/Inbox.md for the complete transactional setup.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EfCoreInboxStoreOptions),
            moduleBuilder.Options));

        if (moduleBuilder.RegisterSaveChangesInterceptor)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(LiteBusInboxSaveChangesInterceptor),
                _ => new LiteBusInboxSaveChangesInterceptor(),
                InstanceLifetime.Singleton));
        }

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
            serviceProvider.GetRequiredService<IInboxEnvelopeFactory>(),
            serviceProvider.GetRequiredService<TimeProvider>())!;
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
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new EfCoreInboxStore(scopeFactory, moduleBuilder.DbContextType!, moduleBuilder.Options);
    }
}