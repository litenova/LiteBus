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
