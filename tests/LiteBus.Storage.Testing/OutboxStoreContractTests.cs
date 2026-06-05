using LiteBus.Outbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared contract tests for outbox store implementations.
/// </summary>
public abstract class OutboxStoreContractTests
{
    /// <summary>
    ///     Gets the UTC timestamp used as a stable clock for lease and visibility assertions.
    /// </summary>
    protected virtual DateTimeOffset BaseTime { get; } = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Creates a store that implements the writer, lease, and state roles for one test run.
    /// </summary>
    /// <returns>The store contracts under test.</returns>
    protected abstract OutboxStoreContracts CreateStore();

    /// <summary>
    ///     Verifies that retry visibility delays subsequent lease attempts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_ShouldRespectRetryVisibility()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(messageId);
        leased[0].Status.Should().Be(OutboxStatus.Publishing);
        leased[0].AttemptCount.Should().Be(1);

        await store.StateWriter.PersistAsync([leased[0].AsFailed("publisher unavailable", now.AddMinutes(5))]);

        var hidden = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);

        await store.StateWriter.PersistAsync([visible[0].AsPublished()]);
    }

    /// <summary>
    ///     Verifies that duplicate idempotency keys return the original stored row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddAsync_ShouldReturnExistingMessageForDuplicateIdempotencyKey()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;
        const string idempotencyKey = "order-submitted-1";

        var first = await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now) with
        {
            IdempotencyKey = idempotencyKey
        });

        var duplicate = await store.Writer.EnqueueAsync(first with
        {
            Id = Guid.NewGuid(),
            Payload = "{\"orderId\":\"2\"}"
        });

        duplicate.Id.Should().Be(first.Id);
        duplicate.Payload.Should().Be(first.Payload);
        duplicate.IdempotencyKey.Should().Be(idempotencyKey);
    }

    /// <summary>
    ///     Verifies that duplicate message identifiers return the original stored row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddAsync_ShouldReturnExistingMessageWhenMessageIdAlreadyExists()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        var first = await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));
        var duplicate = await store.Writer.EnqueueAsync(first with { Payload = "{\"orderId\":\"2\"}" });

        duplicate.Id.Should().Be(first.Id);
        duplicate.Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Verifies that optional metadata fields are persisted on append.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddAsync_ShouldPersistTopicMetadataAndVisibleAfter()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var visibleAfter = BaseTime.AddHours(3);

        var stored = await store.Writer.EnqueueAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            Topic = "orders",
            CreatedAt = BaseTime,
            VisibleAfter = visibleAfter,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1",
            TraceContext = "{\"traceparent\":\"00-def\"}"
        });

        stored.Topic.Should().Be("orders");
        stored.VisibleAfter.Should().Be(visibleAfter);
        stored.CorrelationId.Should().Be("correlation-1");
        stored.TraceContext.Should().Be("{\"traceparent\":\"00-def\"}");
    }

    /// <summary>
    ///     Verifies that future visibility timestamps prevent leasing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseMessage()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now) with
        {
            VisibleAfter = now.AddHours(2)
        });

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddMinutes(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that leasing orders by creation time and respects batch size.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_ShouldOrderByCreatedAtAndRespectBatchSize()
    {
        var store = CreateStore();
        var now = BaseTime;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(firstId, now));
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)));
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2)));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "publisher-1",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        });

        leased.Should().HaveCount(2);
        leased[0].Id.Should().Be(firstId);
        leased[1].Id.Should().Be(secondId);
    }

    /// <summary>
    ///     Verifies that failed messages record retry visibility and error text.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;
        var visibleAfter = now.AddMinutes(15);

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();

        await store.StateWriter.PersistAsync([leased[0].AsFailed("broker down", visibleAfter)]);

        var visible = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-2",
            Now = visibleAfter,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].Status.Should().Be(OutboxStatus.Publishing);
        visible[0].LastError.Should().Be("broker down");
    }

    /// <summary>
    ///     Verifies that dead-lettered messages are not leased again.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.StateWriter.PersistAsync([leased[0].AsDeadLettered("poison message")]);

        var afterDeadLetter = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        afterDeadLetter.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that expired publishing leases can be reclaimed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_WhenLeaseExpires_ShouldReclaimPublishingMessage()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));

        await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = now,
            LeaseDuration = TimeSpan.FromSeconds(20)
        });

        var reclaimed = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
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

    /// <summary>
    ///     Verifies that concurrent lease attempts claim disjoint message sets.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_ConcurrentPublishers_ShouldLeaseDisjointMessages()
    {
        var store = CreateStore();
        var now = BaseTime;

        for (var index = 0; index < 6; index++)
        {
            await store.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index)));
        }

        var request = new OutboxLeaseRequest
        {
            BatchSize = 3,
            LeaseOwner = "publisher",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        var firstBatchTask = store.Lease.LeasePendingAsync(request with { LeaseOwner = "publisher-a" });
        var secondBatchTask = store.Lease.LeasePendingAsync(request with { LeaseOwner = "publisher-b" });
        await Task.WhenAll(firstBatchTask, secondBatchTask);
        var firstBatch = await firstBatchTask;
        var secondBatch = await secondBatchTask;

        var leasedIds = firstBatch.Select(message => message.Id)
            .Concat(secondBatch.Select(message => message.Id))
            .ToArray();

        leasedIds.Should().HaveCount(6);
        leasedIds.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    ///     Verifies that status counts reflect stored messages grouped by status.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task GetStatusCountsAsync_ShouldGroupByStatus()
    {
        var store = CreateStore();
        var pendingId = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(pendingId, now));
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(publishedId, now.AddSeconds(1)));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.StateWriter.PersistAsync([leased[0].AsPublished()]);

        var counts = await store.Diagnostics.GetStatusCountsAsync();

        counts[OutboxStatus.Pending].Should().Be(1);
        counts[OutboxStatus.Published].Should().Be(1);
    }

    /// <summary>
    ///     Verifies that dead-letter replay returns messages to the pending queue.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RequeueDeadLetterAsync_ShouldReturnMessageToPending()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.StateWriter.PersistAsync([leased[0].AsDeadLettered("manual replay")]);

        await store.DeadLetterStore.RequeueAsync(messageId);

        var requeued = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        requeued.Should().ContainSingle();
        requeued[0].Id.Should().Be(messageId);
        requeued[0].Status.Should().Be(OutboxStatus.Publishing);
    }

    /// <summary>
    ///     Creates a pending envelope for contract tests.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    /// <returns>A pending envelope.</returns>
    protected static OutboxEnvelope CreatePendingEnvelope(Guid messageId, DateTimeOffset createdAt)
    {
        return new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }

    /// <summary>
    ///     Holds the outbox store roles exercised by contract tests.
    /// </summary>
    /// <param name="Writer">The writer role.</param>
    /// <param name="Lease">The lease role.</param>
    /// <param name="StateWriter">The state writer role.</param>
    /// <param name="DeadLetterStore">The dead-letter replay role.</param>
    /// <param name="Retention">The retention role.</param>
    /// <param name="Diagnostics">The diagnostics role.</param>
    /// <param name="MessageQuery">The message query role used by browse APIs.</param>
    /// <param name="PurgeStore">The purge role used by operator cleanup.</param>
    public sealed record OutboxStoreContracts(
        IOutboxStore Writer,
        IOutboxLeaseStore Lease,
        IOutboxStateWriter StateWriter,
        IOutboxDeadLetterStore DeadLetterStore,
        IOutboxRetentionStore Retention,
        IOutboxDiagnosticsStore Diagnostics,
        IOutboxMessageQuery MessageQuery,
        IOutboxPurgeStore PurgeStore);

    /// <summary>
    ///     Verifies that message queries filter by status and support keyset pagination.
    /// </summary>
    [Fact]
    public async Task QueryAsync_ShouldFilterAndPageByCreatedAt()
    {
        var store = CreateStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(firstId, now) with { ContractName = "tests.events.a", Topic = "topic.a" });
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)) with { ContractName = "tests.events.b", Topic = "topic.b" });

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.StateWriter.PersistAsync([leased[0].AsPublished()]);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(3)) with { ContractName = "tests.events.a", Topic = "topic.a" });

        var pendingPage = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { Statuses = [OutboxStatus.Pending] },
            new OutboxMessagePageRequest { PageSize = 1 });

        pendingPage.Items.Should().ContainSingle();
        pendingPage.Items[0].Id.Should().Be(secondId);
        pendingPage.HasMore.Should().BeTrue();

        var topicPage = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { Topic = "topic.a" },
            new OutboxMessagePageRequest { PageSize = 10 });

        topicPage.Items.Should().HaveCount(2);
        topicPage.Items.Select(envelope => envelope.Id).Should().BeEquivalentTo([firstId, thirdId]);
    }

    /// <summary>
    ///     Verifies that purge deletes only rows that match the supplied filter.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_ShouldDeleteMatchingRows()
    {
        var store = CreateStore();
        var pendingId = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(pendingId, now));
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(publishedId, now.AddSeconds(1)));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.StateWriter.PersistAsync([leased[0].AsPublished()]);

        var deleted = await store.PurgeStore.PurgeAsync(new OutboxMessageFilter
        {
            Statuses = [OutboxStatus.Published]
        });

        deleted.Should().Be(1);

        var remaining = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 });

        remaining.Items.Should().ContainSingle();
        remaining.Items[0].Id.Should().Be(publishedId);
    }
}
