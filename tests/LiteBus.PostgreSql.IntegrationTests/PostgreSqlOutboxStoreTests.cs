using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlOutboxStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LeasePendingAsync_ShouldRespectRetryVisibility()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(messageId, now));

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].MessageId.Should().Be(messageId);
        leased[0].Status.Should().Be(OutboxMessageStatus.Publishing);
        leased[0].AttemptCount.Should().Be(1);

        await store.MarkFailedAsync(new OutboxMessageFailure
        {
            MessageId = messageId,
            Error = "publisher unavailable",
            VisibleAfter = now.AddMinutes(5)
        });

        var hidden = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);

        await store.MarkPublishedAsync(messageId);

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxMessageStatus.Published);
        row.LeaseOwner.Should().BeNull();
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_ShouldReturnExistingMessageWhenMessageIdAlreadyExists()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        var first = await store.AddAsync(CreatePendingEnvelope(messageId, now));
        var duplicate = await store.AddAsync(first with { Payload = "{\"orderId\":\"2\"}" });

        duplicate.MessageId.Should().Be(first.MessageId);
        duplicate.Payload.Should().Be(first.Payload);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistTopicMetadataAndVisibleAfter()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var visibleAfter = PostgreSqlTestInfrastructure.BaseTime.AddHours(3);

        await store.AddAsync(new OutboxMessageEnvelope
        {
            MessageId = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            Topic = "orders",
            CreatedAt = PostgreSqlTestInfrastructure.BaseTime,
            VisibleAfter = visibleAfter,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1"
        });

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row.Should().NotBeNull();
        row!.Topic.Should().Be("orders");
        row.VisibleAfter.Should().Be(visibleAfter);
        row.CorrelationId.Should().Be("correlation-1");
    }

    [Fact]
    public async Task LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseMessage()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(messageId, now) with
        {
            VisibleAfter = now.AddHours(2)
        });

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddMinutes(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    [Fact]
    public async Task LeasePendingAsync_ShouldOrderByCreatedAtAndRespectBatchSize()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = PostgreSqlTestInfrastructure.BaseTime;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await store.AddAsync(CreatePendingEnvelope(firstId, now));
        await store.AddAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)));
        await store.AddAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2)));

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "publisher-1",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        });

        leased.Should().HaveCount(2);
        leased[0].MessageId.Should().Be(firstId);
        leased[1].MessageId.Should().Be(secondId);
    }

    [Fact]
    public async Task MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;
        var visibleAfter = now.AddMinutes(15);

        await store.AddAsync(CreatePendingEnvelope(messageId, now));

        await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.MarkFailedAsync(new OutboxMessageFailure
        {
            MessageId = messageId,
            Error = "broker down",
            VisibleAfter = visibleAfter
        });

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxMessageStatus.Failed);
        row.VisibleAfter.Should().Be(visibleAfter);
        row.LastError.Should().Be("broker down");
    }

    [Fact]
    public async Task MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(messageId, now));

        await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.MoveToDeadLetterAsync(new OutboxMessageDeadLetter
        {
            MessageId = messageId,
            Reason = "poison message"
        });

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        row.LastError.Should().Be("poison message");

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    [Fact]
    public async Task LeasePendingAsync_WhenLeaseExpires_ShouldReclaimPublishingMessage()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(messageId, now));

        await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = now,
            LeaseDuration = TimeSpan.FromSeconds(20)
        });

        var reclaimed = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fresh-publisher",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        reclaimed.Should().ContainSingle();
        reclaimed[0].LeaseOwner.Should().Be("fresh-publisher");
        reclaimed[0].AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task LeasePendingAsync_ConcurrentPublishers_ShouldLeaseDisjointMessages()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = PostgreSqlTestInfrastructure.BaseTime;

        for (var index = 0; index < 6; index++)
        {
            await store.AddAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index)));
        }

        var request = new OutboxLeaseRequest
        {
            BatchSize = 3,
            LeaseOwner = "publisher",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        var firstBatchTask = store.LeasePendingAsync(request with { LeaseOwner = "publisher-a" });
        var secondBatchTask = store.LeasePendingAsync(request with { LeaseOwner = "publisher-b" });
        await Task.WhenAll(firstBatchTask, secondBatchTask);
        var firstBatch = await firstBatchTask;
        var secondBatch = await secondBatchTask;

        var leasedIds = firstBatch.Select(message => message.MessageId)
            .Concat(secondBatch.Select(message => message.MessageId))
            .ToArray();

        leasedIds.Should().HaveCount(6);
        leasedIds.Should().OnlyHaveUniqueItems();
    }

    private static OutboxMessageEnvelope CreatePendingEnvelope(Guid messageId, DateTimeOffset createdAt)
    {
        return new OutboxMessageEnvelope
        {
            MessageId = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0
        };
    }
}
