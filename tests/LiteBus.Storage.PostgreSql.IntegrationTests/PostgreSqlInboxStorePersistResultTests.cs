using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for explicit <see cref="PersistResult" /> outcomes on PostgreSQL inbox stores.
/// </summary>
public sealed class PostgreSqlInboxStorePersistResultTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxStorePersistResultTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms batch completed persists use envelope timestamps instead of database clock time.
    /// </summary>
    [Fact]
    public async Task PersistAsync_batch_completed_should_use_envelope_completed_at()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstCompletedAt = now.AddMinutes(-7);
        var secondCompletedAt = now.AddMinutes(-3);

        await store.AddAsync(CreatePending(firstId, now));
        await store.AddAsync(CreatePending(secondId, now.AddSeconds(1)));

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "batch-complete",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        var result = await store.PersistAsync(
        [
            leased.Single(envelope => envelope.Id == firstId).AsCompleted() with { CompletedAt = firstCompletedAt },
            leased.Single(envelope => envelope.Id == secondId).AsCompleted() with { CompletedAt = secondCompletedAt }
        ]);

        result.AppliedCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);

        var firstRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, firstId);
        var secondRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, secondId);

        firstRow!.CompletedAt.Should().BeCloseTo(firstCompletedAt, TimeSpan.FromMicroseconds(1));
        secondRow!.CompletedAt.Should().BeCloseTo(secondCompletedAt, TimeSpan.FromMicroseconds(1));
    }

    /// <summary>
    ///     Confirms terminal persist reports lease loss when another worker reclaimed the row.
    /// </summary>
    [Fact]
    public async Task PersistAsync_when_lease_reclaimed_should_report_lease_lost()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePending(messageId, now));

        var firstLease = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-a",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromSeconds(10)
        });

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-b",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        var result = await store.PersistAsync([firstLease[0].AsCompleted()]);

        result.AppliedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
    }

    private static InboxEnvelope CreatePending(Guid id, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = id,
            ContractName = "tests.commands.persist",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        };
    }
}
