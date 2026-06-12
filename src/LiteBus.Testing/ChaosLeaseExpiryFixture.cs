using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Testing;

/// <summary>
///     Chaos fixture that expires a lease while dispatch is in progress.
/// </summary>
public sealed class ChaosLeaseExpiryFixture
{
    /// <summary>
    ///     Gets the backing in-memory store that owns envelope state.
    /// </summary>
    private readonly InMemoryInboxStore _inner;

    /// <summary>
    ///     Gets the message identifier whose lease should expire mid-dispatch.
    /// </summary>
    private readonly Guid _targetMessageId;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChaosLeaseExpiryFixture" /> class.
    /// </summary>
    /// <param name="inner">The backing in-memory store that owns envelope state.</param>
    /// <param name="targetMessageId">The message identifier whose lease should expire mid-dispatch.</param>
    public ChaosLeaseExpiryFixture(InMemoryInboxStore inner, Guid targetMessageId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _targetMessageId = targetMessageId;
    }

    /// <summary>
    ///     Creates a lease store that forces lease renewal to fail for the target message.
    /// </summary>
    /// <returns>The chaos lease store.</returns>
    public IInboxLeaseStore CreateLeaseStore()
    {
        return new ChaosLeaseStore(_inner, _targetMessageId);
    }

    /// <summary>
    ///     Lease store that reports renewal failure for one message to simulate lease loss mid-dispatch.
    /// </summary>
    private sealed class ChaosLeaseStore : IInboxLeaseStore
    {
        /// <summary>
        ///     The backing store used for leasing operations.
        /// </summary>
        private readonly InMemoryInboxStore _inner;

        /// <summary>
        ///     The message whose lease renewal should fail.
        /// </summary>
        private readonly Guid _targetMessageId;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ChaosLeaseStore" /> class.
        /// </summary>
        /// <param name="inner">The backing store used for leasing operations.</param>
        /// <param name="targetMessageId">The message whose lease renewal should fail.</param>
        public ChaosLeaseStore(InMemoryInboxStore inner, Guid targetMessageId)
        {
            _inner = inner;
            _targetMessageId = targetMessageId;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return _inner.LeasePendingAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.MessageId == _targetMessageId)
            {
                return Task.FromResult(false);
            }

            return _inner.RenewLeaseAsync(request, cancellationToken);
        }
    }
}