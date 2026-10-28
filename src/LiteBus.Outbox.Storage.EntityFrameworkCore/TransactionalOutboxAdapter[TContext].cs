using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Exposes <see cref="ITransactionalOutbox{TContext}" /> through the non-generic
///     <see cref="ITransactionalOutbox" /> contract so mediation completion handlers can stage domain events into the
///     ambient Entity Framework Core transaction.
/// </summary>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
/// <remarks>
///     Registered by <see cref="EfCoreOutboxStorageModule" /> when the save-changes interceptor is enabled. Every call
///     delegates to the scoped generic writer, so staged envelopes flush with the same <see cref="DbContext" />
///     that owns the caller unit of work.
/// </remarks>
internal sealed class TransactionalOutboxAdapter<TContext> : ITransactionalOutbox
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the scoped generic writer that stages envelopes through the save-changes interceptor.
    /// </summary>
    private readonly ITransactionalOutbox<TContext> _inner;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalOutboxAdapter{TContext}" /> class.
    /// </summary>
    /// <param name="inner">The scoped generic writer that stages envelopes.</param>
    public TransactionalOutboxAdapter(ITransactionalOutbox<TContext> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _inner.EnqueueAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _inner.EnqueueAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        return _inner.EnqueueBatchAsync(items, cancellationToken);
    }
}
