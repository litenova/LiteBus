using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.UnitTests;

internal static class InboxTestInfrastructure
{
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset initial)
        {
            _utcNow = initial;
        }

        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            _utcNow = value;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    internal sealed class ThrowingInboxLeaseStore : ICommandInboxLeaseStore
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;
        private readonly CommandInboxTests.InMemoryCommandInboxStore _inner = new();

        public ThrowingInboxLeaseStore(int failuresBeforeSuccess = int.MaxValue)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public CommandInboxTests.InMemoryCommandInboxStore Inner => _inner;

        public Task<IReadOnlyList<InboxCommandEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_attempts++ < _failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated lease store failure.");
            }

            return _inner.LeasePendingAsync(request, cancellationToken);
        }
    }

    internal sealed class DelegatingInboxLeaseStore : ICommandInboxLeaseStore
    {
        private readonly CommandInboxTests.InMemoryCommandInboxStore _inner;
        private readonly Func<InboxLeaseRequest, IReadOnlyList<InboxCommandEnvelope>>? _onLease;

        public DelegatingInboxLeaseStore(
            CommandInboxTests.InMemoryCommandInboxStore inner,
            Func<InboxLeaseRequest, IReadOnlyList<InboxCommandEnvelope>>? onLease = null)
        {
            _inner = inner;
            _onLease = onLease;
        }

        public async Task<IReadOnlyList<InboxCommandEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            var leased = await _inner.LeasePendingAsync(request, cancellationToken).ConfigureAwait(false);
            return _onLease?.Invoke(request) ?? leased;
        }
    }
}
