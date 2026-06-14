using LiteBus.Outbox.Abstractions;

namespace LiteBus.Storage.UnitTests.InMemory.Outbox;

/// <summary>
///     Verifies filtered <see cref="InMemoryOutboxStore.GetAll" /> overloads.
/// </summary>
public sealed class InMemoryOutboxGetAllFilterTests
{
    /// <summary>
    ///     Confirms status and contract name filters return only matching envelopes.
    /// </summary>
    [Fact]
    public async Task GetAll_WithStatusAndContractName_ShouldFilterResults()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;

        var pendingMatchId = Guid.NewGuid();
        var pendingOtherContractId = Guid.NewGuid();
        var publishedMatchId = Guid.NewGuid();

        await store.EnqueueAsync(CreateEnvelope(publishedMatchId, "contract.a", OutboxStatus.Pending, now)).ConfigureAwait(false);

        var publishedLease = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "test",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.PersistAsync([publishedLease[0].AsPublished()]).ConfigureAwait(false);

        await store.EnqueueAsync(CreateEnvelope(pendingMatchId, "contract.a", OutboxStatus.Pending, now)).ConfigureAwait(false);
        await store.EnqueueAsync(CreateEnvelope(pendingOtherContractId, "contract.b", OutboxStatus.Pending, now)).ConfigureAwait(false);

        store.GetAll(OutboxStatus.Pending, "contract.a")
            .Should()
            .ContainSingle(envelope => envelope.Id == pendingMatchId);

        store.GetAll(OutboxStatus.Published, "contract.a")
            .Should()
            .ContainSingle(envelope => envelope.Id == publishedMatchId);

        store.GetAll(OutboxStatus.Pending).Should().HaveCount(2);
        store.GetAll("contract.b").Should().ContainSingle(envelope => envelope.Id == pendingOtherContractId);
    }

    private static OutboxEnvelope CreateEnvelope(
        Guid id,
        string contractName,
        OutboxStatus status,
        DateTimeOffset createdAt)
    {
        return new OutboxEnvelope
        {
            Id = id,
            ContractName = contractName,
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            Status = status,
            AttemptCount = 0
        };
    }
}