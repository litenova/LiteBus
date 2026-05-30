using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the Entity Framework Core inbox store with LiteBus dependency injection.
/// </summary>
public sealed class EfCoreInboxStorageModule : IModule
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
            throw new InvalidOperationException(
                "An inbox database context must be configured. Call UseDbContext<TContext>() on the EF Core inbox storage builder.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EfCoreInboxStoreOptions),
            moduleBuilder.Options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxLeaseStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxStateStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder)));
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
