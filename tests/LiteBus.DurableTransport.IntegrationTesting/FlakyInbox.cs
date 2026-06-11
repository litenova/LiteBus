using LiteBus.Inbox.Abstractions;

namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Inbox decorator that throws a configured exception for the first N accept attempts.
/// </summary>
public sealed class FlakyInbox : IInbox
{
    /// <summary>
    ///     Gets the exception thrown until the failure budget is exhausted.
    /// </summary>
    private readonly Exception _failure;

    /// <summary>
    ///     Gets the number of accept attempts that should fail before delegating to the inner inbox.
    /// </summary>
    private readonly int _failureBudget;

    /// <summary>
    ///     Gets the inner inbox receiving successful accept calls.
    /// </summary>
    private readonly IInbox _inner;

    /// <summary>
    ///     Gets the number of accept attempts observed.
    /// </summary>
    private int _attempts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FlakyInbox" /> class.
    /// </summary>
    /// <param name="inner">The inner inbox receiving successful accept calls.</param>
    /// <param name="failure">The exception thrown until the failure budget is exhausted.</param>
    /// <param name="failureBudget">The number of accept attempts that should fail.</param>
    public FlakyInbox(IInbox inner, Exception failure, int failureBudget = 1)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _failure = failure ?? throw new ArgumentNullException(nameof(failure));
        _failureBudget = failureBudget;
    }

    /// <inheritdoc />
    public Task<InboxReceipt> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        if (ShouldFail())
        {
            return Task.FromException<InboxReceipt>(_failure);
        }

        return _inner.AcceptAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail())
        {
            return Task.FromException<IReadOnlyList<InboxReceipt>>(_failure);
        }

        return _inner.AcceptBatchAsync(items, cancellationToken);
    }

    /// <summary>
    ///     Determines whether the current attempt should throw the configured failure.
    /// </summary>
    /// <returns><see langword="true" /> when the failure budget has not been exhausted.</returns>
    private bool ShouldFail()
    {
        return Interlocked.Increment(ref _attempts) <= _failureBudget;
    }
}