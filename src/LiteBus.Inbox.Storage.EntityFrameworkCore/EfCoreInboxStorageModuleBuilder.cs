using System;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Configures the Entity Framework Core inbox storage module.
/// </summary>
public sealed class EfCoreInboxStorageModuleBuilder
{
    /// <summary>
    ///     Gets the database context type that implements <see cref="IInboxDbContext" />.
    /// </summary>
    public Type? DbContextType { get; private set; }

    /// <summary>
    ///     Gets the Entity Framework Core store options.
    /// </summary>
    public EfCoreInboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="LiteBusInboxSaveChangesInterceptor" /> is registered in dependency
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
    public EfCoreInboxStorageModuleBuilder EnforceTransactionalSetup()
    {
        RequireTransactionalSetup = true;
        return this;
    }

    /// <summary>
    ///     Registers <see cref="LiteBusInboxSaveChangesInterceptor" /> as a singleton for use with application
    ///     <see cref="DbContext" /> configuration.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The interceptor is not attached to a <see cref="DbContext" /> automatically. Call
    ///     <see
    ///         cref="InboxDbContextExtensions.AddLiteBusInboxInterceptor(DbContextOptionsBuilder, LiteBusInboxSaveChangesInterceptor)" />
    ///     when building context options, for example
    ///     <c>options.AddLiteBusInboxInterceptor(interceptor)</c>.
    /// </remarks>
    public EfCoreInboxStorageModuleBuilder EnableSaveChangesInterceptor()
    {
        RegisterSaveChangesInterceptor = true;
        return this;
    }

    /// <summary>
    ///     Configures the application database context type used by the inbox store.
    /// </summary>
    /// <typeparam name="TContext">The database context type.</typeparam>
    /// <returns>The current builder.</returns>
    public EfCoreInboxStorageModuleBuilder UseDbContext<TContext>()
        where TContext : DbContext, IInboxDbContext
    {
        DbContextType = typeof(TContext);
        return this;
    }

    /// <summary>
    ///     Replaces the Entity Framework Core store options.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <returns>The current builder.</returns>
    public EfCoreInboxStorageModuleBuilder UseOptions(EfCoreInboxStoreOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}