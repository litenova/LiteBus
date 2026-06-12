using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Storage.InMemory.UnitTests;

/// <summary>
///     Verifies filtered <see cref="InMemoryInboxStore.GetAll" /> overloads.
/// </summary>
public sealed class InMemoryInboxGetAllFilterTests
{
    /// <summary>
    ///     Confirms status and contract name filters return only matching envelopes.
    /// </summary>
    [Fact]
    public async Task GetAll_WithStatusAndContractName_ShouldFilterResults()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;

        var pendingMatchId = Guid.NewGuid();
        var pendingOtherContractId = Guid.NewGuid();
        var completedMatchId = Guid.NewGuid();

        await store.EnqueueAsync(CreateEnvelope(completedMatchId, "contract.a", InboxStatus.Pending, now)).ConfigureAwait(false);

        var completedLease = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "test",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.PersistAsync([completedLease[0].AsCompleted()]).ConfigureAwait(false);

        await store.EnqueueAsync(CreateEnvelope(pendingMatchId, "contract.a", InboxStatus.Pending, now)).ConfigureAwait(false);
        await store.EnqueueAsync(CreateEnvelope(pendingOtherContractId, "contract.b", InboxStatus.Pending, now)).ConfigureAwait(false);

        store.GetAll(InboxStatus.Pending, "contract.a")
            .Should()
            .ContainSingle(envelope => envelope.Id == pendingMatchId);

        store.GetAll(InboxStatus.Completed, "contract.a")
            .Should()
            .ContainSingle(envelope => envelope.Id == completedMatchId);

        store.GetAll(InboxStatus.Pending).Should().HaveCount(2);
        store.GetAll("contract.b").Should().ContainSingle(envelope => envelope.Id == pendingOtherContractId);
    }

    private static InboxEnvelope CreateEnvelope(
        Guid id,
        string contractName,
        InboxStatus status,
        DateTimeOffset createdAt)
    {
        return new InboxEnvelope
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