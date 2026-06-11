using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Verifies lease-guarded Entity Framework Core terminal persist rejects stale writers.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxStoreConcurrencyTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public EfCoreOutboxStoreConcurrencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms only one concurrent terminal persist succeeds when two workers claim the same lease generation.
    /// </summary>
    [Fact]
    public async Task PersistAsync_WhenTwoWorkersPersistSameEnvelopeConcurrently_ShouldApplyExactlyOnce()
    {
        await EfCorePostgreSqlTestInfrastructure.ResetOutboxTableAsync(_fixture.ConnectionString);

        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCorePostgreSqlTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString)),
            EfCorePostgreSqlTestInfrastructure.OutboxStoreOptions);

        var messageId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"1"}""",
            Topic = "tests",
            CreatedAt = now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        });

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-a",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        var publishing = leased[0];

        var winner = publishing.AsPublished();
        var stale = publishing with { LeaseOwner = "publisher-b" };
        stale = stale.AsPublished();

        var firstPersist = store.PersistAsync([winner]);
        var secondPersist = store.PersistAsync([stale]);
        await Task.WhenAll(firstPersist, secondPersist);

        var firstResult = await firstPersist;
        var secondResult = await secondPersist;

        (firstResult.AppliedCount + secondResult.AppliedCount).Should().Be(1);

        await using var verificationContext = EfCorePostgreSqlTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString);
        var stored = await verificationContext.OutboxMessages.FindAsync(messageId);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(OutboxStatus.Published);
        stored.LeaseOwner.Should().BeNull();
    }
}