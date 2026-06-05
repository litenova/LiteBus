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
    ///     Registers writer, lease, state, dead-letter, retention, and diagnostics inbox roles for the same store instance.
    /// </summary>
    /// <typeparam name="TStore">The concrete store type that implements all inbox roles.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="store">The store instance shared by all roles.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInboxStoreRoles<TStore>(this IServiceCollection services, TStore store)
        where TStore : class, IInboxProcessingStore, IInboxOperationsStore
    {
        services.AddSingleton<IInboxStore>(store);
        services.AddSingleton<IInboxLeaseStore>(store);
        services.AddSingleton<IInboxStateWriter>(store);
        services.AddSingleton<IInboxDeadLetterStore>(store);
        services.AddSingleton<IInboxRetentionStore>(store);
        services.AddSingleton<IInboxDiagnosticsStore>(store);
        services.AddSingleton<IInboxMessageQuery>(store);
        services.AddSingleton<IInboxPurgeStore>(store);
        services.AddSingleton<IInboxProcessingStore>(store);
        services.AddSingleton<IInboxOperationsStore>(store);
        return services;
    }

    /// <summary>
    ///     Registers writer, lease, state, dead-letter, retention, and diagnostics outbox roles for the same store instance.
    /// </summary>
    /// <typeparam name="TStore">The concrete store type that implements all outbox roles.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="store">The store instance shared by all roles.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxStoreRoles<TStore>(this IServiceCollection services, TStore store)
        where TStore : class, IOutboxProcessingStore, IOutboxOperationsStore
    {
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IOutboxLeaseStore>(store);
        services.AddSingleton<IOutboxStateWriter>(store);
        services.AddSingleton<IOutboxDeadLetterStore>(store);
        services.AddSingleton<IOutboxRetentionStore>(store);
        services.AddSingleton<IOutboxDiagnosticsStore>(store);
        services.AddSingleton<IOutboxMessageQuery>(store);
        services.AddSingleton<IOutboxPurgeStore>(store);
        services.AddSingleton<IOutboxProcessingStore>(store);
        services.AddSingleton<IOutboxOperationsStore>(store);
        return services;
    }
}
