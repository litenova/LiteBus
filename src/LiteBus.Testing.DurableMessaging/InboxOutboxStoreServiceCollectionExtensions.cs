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
        where TStore : class, IInboxStore, IInboxProcessingStore, IInboxOperationsStore
    {
        services.AddSingleton<IInboxStore>(_ => store);
        services.AddSingleton<IInboxLeaseStore>(_ => store);
        services.AddSingleton<IInboxStateWriter>(_ => store);
        services.AddSingleton<IInboxDeadLetterStore>(_ => store);
        services.AddSingleton<IInboxRetentionStore>(_ => store);
        services.AddSingleton<IInboxDiagnosticsStore>(_ => store);
        services.AddSingleton<IInboxMessageQuery>(_ => store);
        services.AddSingleton<IInboxPurgeStore>(_ => store);
        services.AddSingleton<IInboxProcessingStore>(_ => store);
        services.AddSingleton<IInboxOperationsStore>(_ => store);
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
        where TStore : class, IOutboxStore, IOutboxProcessingStore, IOutboxOperationsStore
    {
        services.AddSingleton<IOutboxStore>(_ => store);
        services.AddSingleton<IOutboxLeaseStore>(_ => store);
        services.AddSingleton<IOutboxStateWriter>(_ => store);
        services.AddSingleton<IOutboxDeadLetterStore>(_ => store);
        services.AddSingleton<IOutboxRetentionStore>(_ => store);
        services.AddSingleton<IOutboxDiagnosticsStore>(_ => store);
        services.AddSingleton<IOutboxMessageQuery>(_ => store);
        services.AddSingleton<IOutboxPurgeStore>(_ => store);
        services.AddSingleton<IOutboxProcessingStore>(_ => store);
        services.AddSingleton<IOutboxOperationsStore>(_ => store);
        return services;
    }
}