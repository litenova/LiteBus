using System;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Configures the Entity Framework Core outbox storage module.
/// </summary>
public sealed class EfCoreOutboxStorageModuleBuilder
{
    /// <summary>
    ///     Gets the database context type that implements <see cref="IOutboxDbContext" />.
    /// </summary>
    public Type? DbContextType { get; private set; }

    /// <summary>
    ///     Gets the Entity Framework Core store options.
    /// </summary>
    public EntityFrameworkCoreOutboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="LiteBusOutboxSaveChangesInterceptor" /> is registered in dependency
    ///     injection.
    /// </summary>
    public bool RegisterSaveChangesInterceptor { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnforceTransactionalSetup" /> was called and Build() must fail when the
    ///     save-changes interceptor is not enabled.
    /// </summary>
    public bool RequireTransactionalSetup { get; private set; }

    /// <summary>
    ///     Requires <see cref="EnableSaveChangesInterceptor" /> to be called before the module builds; otherwise Build()
    ///     throws <see cref="LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException" />.
    /// </summary>
    /// <returns>The current builder.</returns>
    public EfCoreOutboxStorageModuleBuilder EnforceTransactionalSetup()
    {
        RequireTransactionalSetup = true;
        return this;
    }

    /// <summary>
    ///     Registers <see cref="LiteBusOutboxSaveChangesInterceptor" /> as a singleton for use with application
    ///     <see cref="DbContext" /> configuration.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The interceptor is not attached to a <see cref="DbContext" /> automatically. Call
    ///     <see
    ///         cref="OutboxDbContextExtensions.AddLiteBusOutboxInterceptor(DbContextOptionsBuilder, LiteBusOutboxSaveChangesInterceptor)" />
    ///     when building context options, for example
    ///     <c>options.AddLiteBusOutboxInterceptor(interceptor)</c>.
    /// </remarks>
    public EfCoreOutboxStorageModuleBuilder EnableSaveChangesInterceptor()
    {
        RegisterSaveChangesInterceptor = true;
        return this;
    }

    /// <summary>
    ///     Configures the application database context type used by the outbox store.
    /// </summary>
    /// <typeparam name="TContext">The database context type.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Register <see cref="IDbContextFactory{TContext}" /> with the application container. The outbox store creates and
    ///     disposes one context per operation through that factory.
    /// </remarks>
    public EfCoreOutboxStorageModuleBuilder UseDbContext<TContext>()
        where TContext : DbContext, IOutboxDbContext
    {
        DbContextType = typeof(TContext);
        return this;
    }

    /// <summary>
    ///     Replaces the Entity Framework Core store options.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <returns>The current builder.</returns>
    public EfCoreOutboxStorageModuleBuilder UseOptions(EntityFrameworkCoreOutboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        return this;
    }
}
