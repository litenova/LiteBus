using LiteBus.Inbox.Abstractions;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Testing;

/// <summary>
///     Lightweight helpers for running one inbox or outbox processor pass in tests.
/// </summary>
public static class ProcessorPassHelpers
{
    /// <summary>
    ///     Resolves <see cref="IInboxProcessor" /> from the provider and runs one processing pass.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled inbox processor.</param>
    /// <param name="cancellationToken">A token that cancels the pass.</param>
    /// <returns>The pass result describing leased, succeeded, failed, and dead-lettered counts.</returns>
    public static Task<ProcessorPassResult> RunInboxPassAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(cancellationToken);
    }

    /// <summary>
    ///     Resolves <see cref="IOutboxProcessor" /> from the provider and runs one publishing pass.
    /// </summary>
    /// <param name="provider">The service provider built with an enabled outbox processor.</param>
    /// <param name="cancellationToken">A token that cancels the pass.</param>
    /// <returns>The pass result describing leased, succeeded, failed, and dead-lettered counts.</returns>
    public static Task<ProcessorPassResult> RunOutboxPassAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRequiredService<IOutboxProcessor>().ProcessPendingAsync(cancellationToken);
    }
}