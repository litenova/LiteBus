using System;
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

    /// <summary>
    ///     Registers one shared <see cref="TestOutboxStore" /> for every outbox role plus an in-memory <see cref="ITransactionalOutbox" /> for application tests.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="store">The store instance shared by the writer, processor, diagnostics, and transactional roles.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     Call this after <c>AddLiteBus</c> so the test roles win over module registrations. The transactional outbox
    ///     is the production <see cref="StoreBoundTransactionalOutbox" /> bound to the shared store; it writes
    ///     immediately because the in-memory backend has no transaction to enlist in, so it proves enqueue and
    ///     processor behaviour, not rollback.
    /// </remarks>
    public static IServiceCollection AddTestTransactionalOutbox(this IServiceCollection services, TestOutboxStore store)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(store);

        services.AddOutboxStoreRoles(store);
        services.AddSingleton(store);
        services.AddSingleton<ITransactionalOutboxStore>(_ => store);
        services.AddScoped<ITransactionalOutbox>(provider => new StoreBoundTransactionalOutbox(
            provider.GetRequiredService<ITransactionalOutboxStore>(),
            provider.GetRequiredService<IOutboxEnvelopeFactory>()));
        return services;
    }

    /// <summary>
    ///     Registers a new shared <see cref="TestOutboxStore" /> for every outbox role plus an in-memory <see cref="ITransactionalOutbox" /> for application tests.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     Call this after <c>AddLiteBus</c> so the test roles win over module registrations. Resolve
    ///     <see cref="TestOutboxStore" /> from the provider to assert on stored envelopes.
    /// </remarks>
    public static IServiceCollection AddTestTransactionalOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddTestTransactionalOutbox(new TestOutboxStore());
    }
}
