using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

internal static class OutboxTestInfrastructure
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

    internal sealed class ThrowingOutboxLeaseStore : IOutboxMessageLeaseStore
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;
        private readonly OutboxTests.InMemoryOutboxStore _inner = new();

        public ThrowingOutboxLeaseStore(int failuresBeforeSuccess = int.MaxValue)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public OutboxTests.InMemoryOutboxStore Inner => _inner;

        public Task<IReadOnlyList<OutboxMessageEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_attempts++ < _failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated lease store failure.");
            }

            return _inner.LeasePendingAsync(request, cancellationToken);
        }
    }
}
