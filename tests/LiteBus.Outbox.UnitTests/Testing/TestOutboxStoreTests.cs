using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Outbox.Storage.InMemory.Exceptions;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies the store option and clock wiring of <see cref="TestOutboxStore" />.
/// </summary>
[Collection("Sequential")]
public sealed class TestOutboxStoreTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the capacity from store options is enforced by the double.
    /// </summary>
    [Fact]
    public async Task AddAsync_should_enforce_capacity_from_store_options()
    {
        var store = new TestOutboxStore(new InMemoryOutboxStoreOptions { Capacity = 1 });

        await store.AddAsync(PendingEnvelope(visibleAfter: null)).ConfigureAwait(false);

        Func<Task> second = () => store.AddAsync(PendingEnvelope(visibleAfter: null));

        await second.Should().ThrowAsync<OutboxStorageException>().WithMessage("*capacity*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that lease visibility follows the configured clock when the lease request omits a timestamp.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_should_use_the_configured_clock_when_the_request_omits_now()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        var store = new TestOutboxStore(timeProvider: clock);

        await store.AddAsync(PendingEnvelope(visibleAfter: start.AddHours(1))).ConfigureAwait(false);

        var hidden = await store.LeasePendingAsync(LeaseRequest()).ConfigureAwait(false);
        hidden.Should().BeEmpty();

        clock.Advance(TimeSpan.FromHours(2));

        var leased = await store.LeasePendingAsync(LeaseRequest()).ConfigureAwait(false);
        leased.Should().ContainSingle();
    }

    /// <summary>
    ///     Builds a pending envelope with a unique identifier.
    /// </summary>
    /// <param name="visibleAfter">The earliest timestamp at which the envelope may be leased, if any.</param>
    /// <returns>The pending envelope.</returns>
    private static OutboxEnvelope PendingEnvelope(DateTimeOffset? visibleAfter)
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.placed",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            VisibleAfter = visibleAfter,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        };
    }

    /// <summary>
    ///     Builds a lease request that omits an explicit timestamp so the store clock applies.
    /// </summary>
    /// <returns>The lease request.</returns>
    private static OutboxLeaseRequest LeaseRequest()
    {
        return new OutboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = "test-worker",
            Now = default,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };
    }
}
