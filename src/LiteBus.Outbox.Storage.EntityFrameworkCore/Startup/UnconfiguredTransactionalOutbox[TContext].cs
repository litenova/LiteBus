using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.Startup;

/// <summary>
///     Reports a transactional outbox requested for a database context the EF Core outbox storage module did not configure.
/// </summary>
/// <typeparam name="TContext">The requested database context type.</typeparam>
/// <remarks>
///     Registered as the open-generic <see cref="ITransactionalOutbox{TContext}" /> binding when the save-changes
///     interceptor is enabled. The closed binding for the configured context wins for that context, so this type only
///     activates for a context with no <c>UseDbContext&lt;TContext&gt;()</c> call, where it fails fast with the missing
///     call named instead of failing on the first enqueue.
/// </remarks>
internal sealed class UnconfiguredTransactionalOutbox<TContext> : ITransactionalOutbox<TContext>
    where TContext : class
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="UnconfiguredTransactionalOutbox{TContext}" /> class.
    /// </summary>
    /// <exception cref="LiteBusConfigurationException">Always thrown: the requested context is not configured.</exception>
    public UnconfiguredTransactionalOutbox()
    {
        throw CreateNotConfiguredException();
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        throw CreateNotConfiguredException();
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        throw CreateNotConfiguredException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        throw CreateNotConfiguredException();
    }

    /// <summary>
    ///     Creates the configuration error naming the <c>UseDbContext</c> call the requested context is missing.
    /// </summary>
    /// <returns>The configuration error for the unconfigured context.</returns>
    private static LiteBusConfigurationException CreateNotConfiguredException()
    {
        var contextName = typeof(TContext).Name;

        return new LiteBusConfigurationException(
            $"ITransactionalOutbox<{contextName}> was requested, but the Entity Framework Core outbox storage " +
            $"module did not configure database context '{contextName}'. " +
            $"Call UseDbContext<{contextName}>() on the EF Core outbox storage builder to configure it.");
    }
}
