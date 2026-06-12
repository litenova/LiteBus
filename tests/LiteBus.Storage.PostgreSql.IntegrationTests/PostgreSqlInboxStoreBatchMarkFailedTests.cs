using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for PostgreSQL inbox batch failure updates.
/// </summary>
public sealed class PostgreSqlInboxStoreBatchMarkFailedTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxStoreBatchMarkFailedTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms batch <see cref="IInboxStateWriter.PersistAsync" /> accepts all-null visible-after timestamps.
    /// </summary>
    [Fact]
    public async Task PersistAsync_batch_with_null_visible_after_should_persist_failed_status()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await store.EnqueueAsync(CreatePending(firstId, now)).ConfigureAwait(false);
        await store.EnqueueAsync(CreatePending(secondId, now.AddSeconds(1))).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "batch-fail",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.PersistAsync(
        [
            leased.Single(envelope => envelope.Id == firstId).AsFailed("e1"),
            leased.Single(envelope => envelope.Id == secondId).AsFailed("e2")
        ]).ConfigureAwait(false);

        var firstRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, firstId).ConfigureAwait(false);
        var secondRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, secondId).ConfigureAwait(false);

        firstRow!.Status.Should().Be(InboxStatus.Failed);
        firstRow.VisibleAfter.Should().BeNull();
        firstRow.LastError.Should().Be("e1");

        secondRow!.Status.Should().Be(InboxStatus.Failed);
        secondRow.VisibleAfter.Should().BeNull();
        secondRow.LastError.Should().Be("e2");
    }

    private static InboxEnvelope CreatePending(Guid id, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = id,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        };
    }
}