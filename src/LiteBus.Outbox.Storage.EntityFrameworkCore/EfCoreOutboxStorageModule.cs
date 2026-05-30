using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the Entity Framework Core outbox store with LiteBus dependency injection.
/// </summary>
public sealed class EfCoreOutboxStorageModule : IModule
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
            throw new InvalidOperationException(
                "An outbox database context must be configured. Call UseDbContext<TContext>() on the EF Core outbox storage builder.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(EfCoreOutboxStoreOptions),
            moduleBuilder.Options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxLeaseStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStateStore),
            serviceProvider => CreateStore(serviceProvider, moduleBuilder)));
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
