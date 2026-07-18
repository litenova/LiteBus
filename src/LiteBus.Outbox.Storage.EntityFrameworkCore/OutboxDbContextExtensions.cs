using System;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Provides <see cref="DbContext" /> registration helpers for LiteBus outbox transaction integration.
/// </summary>
public static class OutboxDbContextExtensions
{
    /// <summary>
    ///     Registers <see cref="LiteBusOutboxSaveChangesInterceptor" /> on the current options builder.
    /// </summary>
    /// <param name="optionsBuilder">The options builder used to configure a database context.</param>
    /// <param name="interceptor">The interceptor that flushes queued outbox envelopes during <c>SaveChanges</c>.</param>
    /// <returns>The same options builder for call chaining.</returns>
    public static DbContextOptionsBuilder AddLiteBusOutboxInterceptor(
        this DbContextOptionsBuilder optionsBuilder,
        LiteBusOutboxSaveChangesInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(interceptor);

        optionsBuilder.AddInterceptors(interceptor);
        return optionsBuilder;
    }

    /// <summary>
    ///     Registers <see cref="LiteBusOutboxSaveChangesInterceptor" /> on the current typed options builder.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The typed options builder used to configure a database context.</param>
    /// <param name="interceptor">The interceptor that flushes queued outbox envelopes during <c>SaveChanges</c>.</param>
    /// <returns>The same options builder for call chaining.</returns>
    public static DbContextOptionsBuilder<TContext> AddLiteBusOutboxInterceptor<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        LiteBusOutboxSaveChangesInterceptor interceptor)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(interceptor);

        optionsBuilder.AddInterceptors(interceptor);
        return optionsBuilder;
    }
}