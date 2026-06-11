using System;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Provides <see cref="DbContext" /> registration helpers for LiteBus inbox transaction integration.
/// </summary>
public static class InboxDbContextExtensions
{
    /// <summary>
    ///     Registers <see cref="LiteBusInboxSaveChangesInterceptor" /> on the current options builder.
    /// </summary>
    /// <param name="optionsBuilder">The options builder used to configure a database context.</param>
    /// <param name="interceptor">The interceptor that flushes queued inbox envelopes during <c>SaveChanges</c>.</param>
    /// <returns>The same options builder for call chaining.</returns>
    public static DbContextOptionsBuilder AddLiteBusInboxInterceptor(
        this DbContextOptionsBuilder optionsBuilder,
        LiteBusInboxSaveChangesInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(interceptor);

        optionsBuilder.AddInterceptors(interceptor);
        return optionsBuilder;
    }

    /// <summary>
    ///     Registers <see cref="LiteBusInboxSaveChangesInterceptor" /> on the current typed options builder.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The typed options builder used to configure a database context.</param>
    /// <param name="interceptor">The interceptor that flushes queued inbox envelopes during <c>SaveChanges</c>.</param>
    /// <returns>The same options builder for call chaining.</returns>
    public static DbContextOptionsBuilder<TContext> AddLiteBusInboxInterceptor<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        LiteBusInboxSaveChangesInterceptor interceptor)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(interceptor);

        optionsBuilder.AddInterceptors(interceptor);
        return optionsBuilder;
    }
}