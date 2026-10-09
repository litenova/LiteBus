using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for PostgreSQL requeue column resets.
/// </summary>
public sealed class PostgreSqlRequeueColumnTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlRequeueColumnTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms an inbox requeue clears failure columns while keeping the last attempt timestamp.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_WhenInboxMessageIsRequeued_ShouldClearFailureColumnsAndKeepLastAttemptedAt()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.requeue",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        var failureAt = now.AddMinutes(1);
        await store.PersistAsync([leased[0].AsDeadLettered(MessageFailure.From(new InvalidOperationException("boom"), failureAt))]).ConfigureAwait(false);

        var deadLettered = await QueryInboxAsync(store, messageId).ConfigureAwait(false);
        deadLettered.Status.Should().Be(InboxStatus.DeadLettered);
        deadLettered.ErrorType.Should().Be(typeof(InvalidOperationException).FullName);
        deadLettered.FirstFailedAt.Should().Be(failureAt);
        deadLettered.DeadLetteredAt.Should().Be(failureAt);
        deadLettered.LastAttemptedAt.Should().NotBeNull();

        var result = await store.RequeueAsync([messageId]).ConfigureAwait(false);
        result.Requeued.Should().Be(1);

        var requeued = await QueryInboxAsync(store, messageId).ConfigureAwait(false);
        requeued.Status.Should().Be(InboxStatus.Pending);
        requeued.AttemptCount.Should().Be(0);
        requeued.LastError.Should().BeNull();
        requeued.ErrorType.Should().BeNull();
        requeued.FirstFailedAt.Should().BeNull();
        requeued.DeadLetteredAt.Should().BeNull();
        requeued.LastAttemptedAt.Should().Be(deadLettered.LastAttemptedAt);
    }

    /// <summary>
    ///     Confirms an outbox requeue clears failure columns while keeping the last attempt timestamp.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_WhenOutboxMessageIsRequeued_ShouldClearFailureColumnsAndKeepLastAttemptedAt()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.requeue",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        var failureAt = now.AddMinutes(1);
        await store.PersistAsync([leased[0].AsDeadLettered(MessageFailure.From(new InvalidOperationException("boom"), failureAt))]).ConfigureAwait(false);

        var deadLettered = await QueryOutboxAsync(store, messageId).ConfigureAwait(false);
        deadLettered.Status.Should().Be(OutboxStatus.DeadLettered);
        deadLettered.ErrorType.Should().Be(typeof(InvalidOperationException).FullName);
        deadLettered.FirstFailedAt.Should().Be(failureAt);
        deadLettered.DeadLetteredAt.Should().Be(failureAt);
        deadLettered.LastAttemptedAt.Should().NotBeNull();

        var result = await store.RequeueAsync([messageId]).ConfigureAwait(false);
        result.Requeued.Should().Be(1);

        var requeued = await QueryOutboxAsync(store, messageId).ConfigureAwait(false);
        requeued.Status.Should().Be(OutboxStatus.Pending);
        requeued.AttemptCount.Should().Be(0);
        requeued.LastError.Should().BeNull();
        requeued.ErrorType.Should().BeNull();
        requeued.FirstFailedAt.Should().BeNull();
        requeued.DeadLetteredAt.Should().BeNull();
        requeued.LastAttemptedAt.Should().Be(deadLettered.LastAttemptedAt);
    }

    private static async Task<InboxEnvelope> QueryInboxAsync(PostgreSqlInboxStore store, Guid messageId)
    {
        var page = await store.QueryAsync(
            new InboxMessageFilter { MessageId = messageId },
            new InboxMessagePageRequest { PageSize = 1 }).ConfigureAwait(false);

        page.Items.Should().ContainSingle();
        return page.Items[0];
    }

    private static async Task<OutboxEnvelope> QueryOutboxAsync(PostgreSqlOutboxStore store, Guid messageId)
    {
        var page = await store.QueryAsync(
            new OutboxMessageFilter { MessageId = messageId },
            new OutboxMessagePageRequest { PageSize = 1 }).ConfigureAwait(false);

        page.Items.Should().ContainSingle();
        return page.Items[0];
    }
}
