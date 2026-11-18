using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.Startup;

/// <summary>
///     Verifies the configured inbox database context resolves from a new scope during host startup.
/// </summary>
/// <typeparam name="TContext">The application database context type configured through <c>UseDbContext&lt;TContext&gt;()</c>.</typeparam>
/// <remarks>
///     The inbox store creates contexts through <see cref="IDbContextFactory{TContext}" />, while transactional
///     acceptance resolves the scoped <typeparamref name="TContext" /> itself. A factory-only registration therefore
///     composes cleanly and fails on the first transactional acceptance; this check fails startup instead with the
///     missing <c>AddDbContext&lt;TContext&gt;</c> call named.
/// </remarks>
public sealed class EfCoreInboxDbContextStartupCheck<TContext> : IStartupTask
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the root provider used to open the scope the check resolves from.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxDbContextStartupCheck{TContext}" /> class.
    /// </summary>
    /// <param name="serviceProvider">The root provider used to open the scope the check resolves from.</param>
    public EfCoreInboxDbContextStartupCheck(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken)
    {
        // The application container's own scope comes first: the context is registered there, and a LiteBus dispatch
        // scope over a root provider would resolve the scoped context from the root.
        if (_serviceProvider.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory serviceScopeFactory)
        {
            using var serviceScope = serviceScopeFactory.CreateScope();

            ThrowWhenUnresolvable(serviceScope.ServiceProvider);
        }
        else if (_serviceProvider.GetService(typeof(IMessageDispatchScopeFactory)) is IMessageDispatchScopeFactory scopeFactory)
        {
            using var scope = scopeFactory.CreateScope();

            ThrowWhenUnresolvable(scope.ServiceProvider);
        }
        else
        {
            ThrowWhenUnresolvable(_serviceProvider);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Throws when the configured database context cannot be resolved from the given provider.
    /// </summary>
    /// <param name="serviceProvider">The provider used for resolution.</param>
    private static void ThrowWhenUnresolvable(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(TContext)) is null)
        {
            var contextName = typeof(TContext).Name;

            throw new DurableStorageConfigurationException(
                $"The Entity Framework Core inbox store is configured with database context '{contextName}', " +
                "but resolving it from a new scope failed. Register the scoped context with the application " +
                $"container, for example services.AddDbContext<{contextName}>(options => ...), so the inbox " +
                "store and transactional writers can resolve it.");
        }
    }
}
