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
    public EfCoreOutboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Configures the application database context type used by the outbox store.
    /// </summary>
    /// <typeparam name="TContext">The database context type.</typeparam>
    /// <returns>The current builder.</returns>
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
    public EfCoreOutboxStorageModuleBuilder UseOptions(EfCoreOutboxStoreOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}
