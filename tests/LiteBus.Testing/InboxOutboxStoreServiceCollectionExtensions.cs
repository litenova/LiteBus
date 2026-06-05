using LiteBus.Inbox.Abstractions;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Testing;

/// <summary>
///     Registers inbox and outbox store role interfaces against one shared store instance for unit tests.
/// </summary>
public static class InboxOutboxStoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers writer, lease, terminal, retention, and diagnostics inbox roles for the same store instance.
    /// </summary>
    /// <typeparam name="TStore">The concrete store type that implements all inbox roles.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="store">The store instance shared by all roles.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInboxStoreRoles<TStore>(this IServiceCollection services, TStore store)
        where TStore : class, IInboxStore, IInboxLeaseStore, IInboxTerminalStateStore, IInboxRetentionStore, IInboxDiagnosticsStore
    {
        services.AddSingleton<IInboxStore>(store);
        services.AddSingleton<IInboxLeaseStore>(store);
        services.AddSingleton<IInboxTerminalStateStore>(store);
        services.AddSingleton<IInboxRetentionStore>(store);
        services.AddSingleton<IInboxDiagnosticsStore>(store);
        return services;
    }

    /// <summary>
    ///     Registers writer, lease, terminal, retention, and diagnostics outbox roles for the same store instance.
    /// </summary>
    /// <typeparam name="TStore">The concrete store type that implements all outbox roles.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="store">The store instance shared by all roles.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxStoreRoles<TStore>(this IServiceCollection services, TStore store)
        where TStore : class, IOutboxStore, IOutboxLeaseStore, IOutboxTerminalStateStore, IOutboxRetentionStore, IOutboxDiagnosticsStore
    {
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IOutboxLeaseStore>(store);
        services.AddSingleton<IOutboxTerminalStateStore>(store);
        services.AddSingleton<IOutboxRetentionStore>(store);
        services.AddSingleton<IOutboxDiagnosticsStore>(store);
        return services;
    }
}
