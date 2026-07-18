using LiteBus.Outbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared contract tests for outbox store implementations.
/// </summary>
public abstract class OutboxStoreContractTests
{
    /// <summary>
    ///     Gets the UTC timestamp used as the baseline for lease and visibility assertions.
    /// </summary>
    protected virtual DateTimeOffset BaseTime { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>
    ///     Creates a store that implements the writer, lease, and state roles for one test run.
    /// </summary>
    /// <returns>The store contracts under test.</returns>
    protected abstract OutboxStoreContracts CreateStore();

    /// <summary>
    ///     Verifies invalid lease inputs are rejected before a store can create an unusable lease.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_WhenRequestIsInvalid_ShouldRejectRequest()
    {
        var store = CreateStore();
        var request = new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };

        var zeroBatch = () => store.Lease.LeasePendingAsync(request with { BatchSize = 0 });
        var blankOwner = () => store.Lease.LeasePendingAsync(request with { LeaseOwner = " " });
        var negativeDuration = () => store.Lease.LeasePendingAsync(
            request with { LeaseDuration = TimeSpan.FromSeconds(-1) });

        await zeroBatch.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await blankOwner.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        await negativeDuration.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that cancellation requested before an append prevents any store mutation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddAsync_WhenCancellationIsRequested_ShouldNotStoreMessage()
    {
        var store = CreateStore();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var append = () => store.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), BaseTime), cancellationSource.Token);

        await append.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var counts = await store.Diagnostics.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Values.Sum().Should().Be(0);
    }

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
        var visibleAfter = DateTimeOffset.UtcNow.AddMilliseconds(500);

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(messageId);
        leased[0].Status.Should().Be(OutboxStatus.Publishing);
        leased[0].AttemptCount.Should().Be(1);

        await store.StateWriter.PersistAsync([leased[0].AsFailed("publisher unavailable", visibleAfter)]).ConfigureAwait(false);

        await WaitUntilVisibleAsync(visibleAfter).ConfigureAwait(false);

        var visible = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = DateTimeOffset.UtcNow,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);

        await store.StateWriter.PersistAsync([visible[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);
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

        var firstResult = await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now) with
        {
            IdempotencyKey = idempotencyKey
        }).ConfigureAwait(false);

        var first = firstResult.Envelope;
        var duplicate = await store.Writer.EnqueueAsync(first with
        {
            Id = Guid.NewGuid(),
            Payload = "{\"orderId\":\"2\"}"
        }).ConfigureAwait(false);

        firstResult.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        duplicate.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        duplicate.Envelope.Id.Should().Be(first.Id);
        duplicate.Envelope.Payload.Should().Be(first.Payload);
        duplicate.Envelope.IdempotencyKey.Should().Be(idempotencyKey);
    }

    /// <summary>
    ///     Verifies that a tenant-scoped lease request does not claim rows owned by another tenant.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_WhenTenantFilterDoesNotMatchStoredTenant_ShouldNotLeaseRow()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now) with
        {
            TenantId = "tenant-b",
            IdempotencyKey = "tenant-b-outbox"
        }).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-a",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            TenantId = "tenant-a"
        }).ConfigureAwait(false);

        leased.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a tenant-scoped message query excludes rows stored for another tenant.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task QueryAsync_WhenTenantFilterDoesNotMatchStoredTenant_ShouldExcludeOtherTenantRows()
    {
        var store = CreateStore();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now) with
        {
            TenantId = "tenant-a",
            IdempotencyKey = "tenant-a-outbox"
        }).ConfigureAwait(false);

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(1)) with
        {
            TenantId = "tenant-b",
            IdempotencyKey = "tenant-b-outbox"
        }).ConfigureAwait(false);

        var tenantAPage = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { TenantId = "tenant-a" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        tenantAPage.Items.Should().ContainSingle();
        tenantAPage.Items[0].TenantId.Should().Be("tenant-a");
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

        var first = await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);
        var duplicate = await store.Writer.EnqueueAsync(
            first.Envelope with { Payload = "{\"orderId\":\"2\"}" }).ConfigureAwait(false);

        first.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        duplicate.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        duplicate.Envelope.Id.Should().Be(first.Envelope.Id);
        duplicate.Envelope.Payload.Should().Be(first.Envelope.Payload);
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
        }).ConfigureAwait(false);

        stored.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        stored.Envelope.Topic.Should().Be("orders");
        stored.Envelope.VisibleAfter.Should().Be(visibleAfter);
        stored.Envelope.CorrelationId.Should().Be("correlation-1");
        stored.Envelope.TraceContext.Should().Be("{\"traceparent\":\"00-def\"}");
    }

    /// <summary>
    ///     Verifies that batch append preserves input order and reports new rows.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_ShouldReturnResultsInInputOrder()
    {
        var store = CreateStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var results = await store.Writer.AddBatchAsync([
            CreatePendingEnvelope(firstId, BaseTime),
            CreatePendingEnvelope(secondId, BaseTime.AddSeconds(1))
        ]).ConfigureAwait(false);

        results.Should().HaveCount(2);
        results[0].Envelope.Id.Should().Be(firstId);
        results[1].Envelope.Id.Should().Be(secondId);
        results.Select(result => result.Outcome).Should()
            .OnlyContain(outcome => outcome == OutboxEnqueueOutcome.Enqueued);
    }

    /// <summary>
    ///     Verifies that a repeated message identifier within one batch reports the later slot as an existing row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_RepeatedMessageIdWithinBatch_ShouldReportExistingOutcome()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var first = CreatePendingEnvelope(messageId, BaseTime) with { IdempotencyKey = null };

        var results = await store.Writer.AddBatchAsync([
            first,
            first with { Payload = "{\"changed\":true}" }
        ]).ConfigureAwait(false);

        results.Should().HaveCount(2);
        results[0].Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        results[1].Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        results[1].Envelope.Payload.Should().Be(first.Payload);
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
        }).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddMinutes(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(firstId, now)).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1))).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2))).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "publisher-1",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        }).ConfigureAwait(false);

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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();

        await store.StateWriter.PersistAsync([leased[0].AsFailed("broker down", visibleAfter)]).ConfigureAwait(false);

        var stored = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { MessageId = messageId },
            new OutboxMessagePageRequest { PageSize = 1 }).ConfigureAwait(false);

        stored.Items.Should().ContainSingle();
        stored.Items[0].Status.Should().Be(OutboxStatus.Failed);
        stored.Items[0].VisibleAfter.Should().Be(visibleAfter);
        stored.Items[0].LastError.Should().Be("broker down");
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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsDeadLettered("poison message")]).ConfigureAwait(false);

        var afterDeadLetter = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

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
        var leaseDuration = TimeSpan.FromMilliseconds(200);

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var staleLease = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher",
            Now = now,
            LeaseDuration = leaseDuration
        }).ConfigureAwait(false);

        staleLease.Should().ContainSingle();
        await Task.Delay(leaseDuration.Add(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

        var reclaimed = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher",
            Now = DateTimeOffset.UtcNow,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        reclaimed.Should().ContainSingle();
        reclaimed[0].LeaseOwner.Should().Be("publisher");
        reclaimed[0].AttemptCount.Should().Be(2);
        reclaimed[0].LeaseGeneration.Should().Be(staleLease[0].LeaseGeneration + 1);

        var stalePersist = await store.StateWriter.PersistAsync([staleLease[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);
        stalePersist.AppliedCount.Should().Be(0);

        var currentPersist = await store.StateWriter.PersistAsync([reclaimed[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);
        currentPersist.AppliedCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that only the active outbox lease owner can renew a lease.
    /// </summary>
    [Fact]
    public async Task RenewLeaseAsync_ShouldRequireActiveLeaseOwner()
    {
        var store = CreateStore();
        var now = BaseTime;
        var messageId = Guid.NewGuid();

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now));
        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-a",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        var renewed = await store.Lease.RenewLeaseAsync(
            new LeaseRenewalRequest(
                messageId,
                "publisher-a",
                leased[0].LeaseGeneration,
                TimeSpan.FromMinutes(1),
                now.AddMinutes(2)));
        var rejected = await store.Lease.RenewLeaseAsync(
            new LeaseRenewalRequest(
                messageId,
                "publisher-b",
                leased[0].LeaseGeneration,
                TimeSpan.FromMinutes(2),
                now.AddMinutes(3)));
        var staleGeneration = await store.Lease.RenewLeaseAsync(
            new LeaseRenewalRequest(
                messageId,
                "publisher-a",
                leased[0].LeaseGeneration - 1,
                TimeSpan.FromMinutes(3),
                now.AddMinutes(4)));

        renewed.Should().BeTrue();
        rejected.Should().BeFalse();
        staleGeneration.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that concurrent lease attempts claim disjoint message sets.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LeasePendingAsync_ConcurrentPublishers_ShouldLeaseDisjointMessages()
    {
        await AssertConcurrentLeasesAreDisjointAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that concurrent lease attempts claim disjoint message sets.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    protected async virtual Task AssertConcurrentLeasesAreDisjointAsync()
    {
        var store = CreateStore();
        var now = BaseTime;

        for (var index = 0; index < 6; index++)
        {
            await store.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index))).ConfigureAwait(false);
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
        await Task.WhenAll(firstBatchTask, secondBatchTask).ConfigureAwait(false);
        var firstBatch = await firstBatchTask.ConfigureAwait(false);
        var secondBatch = await secondBatchTask.ConfigureAwait(false);

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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(pendingId, now)).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(publishedId, now.AddSeconds(1))).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);

        var counts = await store.Diagnostics.GetStatusCountsAsync().ConfigureAwait(false);

        counts[OutboxStatus.Pending].Should().Be(1);
        counts[OutboxStatus.Published].Should().Be(1);
    }

    /// <summary>
    ///     Verifies that only one terminal persist succeeds when competing writers use different lease owners.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PersistAsync_WhenCompetingLeaseOwnersPersistConcurrently_ShouldApplyExactlyOnce()
    {
        var store = CreateStore();
        var messageId = Guid.NewGuid();
        var now = BaseTime;

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-a",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        var publishing = leased[0];

        var winner = publishing.AsPublished(DateTimeOffset.UtcNow);
        var stale = publishing with { LeaseOwner = "publisher-b" };
        stale = stale.AsPublished(DateTimeOffset.UtcNow);

        var firstPersist = store.StateWriter.PersistAsync([winner]);
        var secondPersist = store.StateWriter.PersistAsync([stale]);
        await Task.WhenAll(firstPersist, secondPersist).ConfigureAwait(false);

        var firstResult = await firstPersist.ConfigureAwait(false);
        var secondResult = await secondPersist.ConfigureAwait(false);

        (firstResult.AppliedCount + secondResult.AppliedCount).Should().Be(1);
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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(messageId, now)).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsDeadLettered("manual replay")]).ConfigureAwait(false);

        await store.DeadLetterStore.RequeueAsync(messageId).ConfigureAwait(false);

        var requeued = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        requeued.Should().ContainSingle();
        requeued[0].Id.Should().Be(messageId);
        requeued[0].Status.Should().Be(OutboxStatus.Publishing);
    }

    /// <summary>
    ///     Verifies that an empty batch is a successful no-op for every batch-oriented store role.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EmptyBatchOperations_ShouldReturnEmptyResults()
    {
        var store = CreateStore();

        var appended = await store.Writer.AddBatchAsync([]).ConfigureAwait(false);
        var persisted = await store.StateWriter.PersistAsync([]).ConfigureAwait(false);
        var requeued = await store.DeadLetterStore.RequeueAsync([]).ConfigureAwait(false);

        appended.Should().BeEmpty();
        persisted.AppliedCount.Should().Be(0);
        persisted.SkippedCount.Should().Be(0);
        requeued.Requested.Should().Be(0);
        requeued.Requeued.Should().Be(0);
    }

    /// <summary>
    ///     Verifies one persist batch can apply multiple rows for every terminal outbox status.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PersistAsync_WithMixedTerminalBatch_ShouldApplyEveryTransition()
    {
        var store = CreateStore();
        var now = BaseTime;
        var source = Enumerable.Range(0, 6)
            .Select(index => CreatePendingEnvelope(Guid.NewGuid(), now.AddMilliseconds(index)))
            .ToArray();

        await store.Writer.AddBatchAsync(source).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = source.Length,
            LeaseOwner = "mixed-publisher",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        }).ConfigureAwait(false);

        leased.Should().HaveCount(source.Length);
        var terminal = new[]
        {
            leased[0].AsPublished(now),
            leased[1].AsPublished(now.AddSeconds(1)),
            leased[2].AsFailed("temporary-1", now.AddMinutes(5)),
            leased[3].AsFailed("temporary-2", now.AddMinutes(5)),
            leased[4].AsDeadLettered("poison-1"),
            leased[5].AsDeadLettered("poison-2")
        };

        var result = await store.StateWriter.PersistAsync(terminal).ConfigureAwait(false);
        var page = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { MessageIds = source.Select(envelope => envelope.Id).ToArray() },
            new OutboxMessagePageRequest { PageSize = source.Length }).ConfigureAwait(false);

        result.AppliedCount.Should().Be(source.Length);
        result.SkippedCount.Should().Be(0);
        page.Items.Should().HaveCount(source.Length);
        page.Items.Count(envelope => envelope.Status == OutboxStatus.Published).Should().Be(2);
        page.Items.Count(envelope => envelope.Status == OutboxStatus.Failed).Should().Be(2);
        page.Items.Count(envelope => envelope.Status == OutboxStatus.DeadLettered).Should().Be(2);
    }

    /// <summary>
    ///     Verifies that batch dead-letter replay reports both requested and updated rows.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RequeueDeadLetterAsync_WithMultipleIds_ShouldRequeueEveryMatchingRow()
    {
        var store = CreateStore();
        var now = BaseTime;
        var source = new[]
        {
            CreatePendingEnvelope(Guid.NewGuid(), now),
            CreatePendingEnvelope(Guid.NewGuid(), now.AddMilliseconds(1))
        };

        await store.Writer.AddBatchAsync(source).ConfigureAwait(false);
        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = source.Length,
            LeaseOwner = "requeue-publisher",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        }).ConfigureAwait(false);
        await store.StateWriter.PersistAsync(
            leased.Select(envelope => envelope.AsDeadLettered("manual replay")).ToArray()).ConfigureAwait(false);

        var result = await store.DeadLetterStore.RequeueAsync(
            source.Select(envelope => envelope.Id.ToString("D")).ToArray()).ConfigureAwait(false);

        result.Requested.Should().Be(source.Length);
        result.Requeued.Should().Be(source.Length);
    }

    /// <summary>
    ///     Verifies every outbox message predicate composes consistently for query and purge operations.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CompleteFilter_ShouldMatchOnlyTheIntendedMessage()
    {
        var store = CreateStore();
        var now = BaseTime;
        var target = CreatePendingEnvelope(Guid.NewGuid(), now) with
        {
            ContractName = "tests.events.filtered",
            Topic = "orders.filtered",
            CorrelationId = "correlation-filtered",
            CausationId = "causation-filtered",
            TenantId = "tenant-filtered"
        };
        var other = CreatePendingEnvelope(Guid.NewGuid(), now.AddMinutes(5)) with
        {
            ContractName = "tests.events.other",
            Topic = "orders.other",
            CorrelationId = "correlation-other",
            CausationId = "causation-other",
            TenantId = "tenant-other"
        };
        await store.Writer.AddBatchAsync([target, other]).ConfigureAwait(false);

        var filter = new OutboxMessageFilter
        {
            MessageId = target.Id,
            MessageIds = [target.Id, other.Id],
            Statuses = [OutboxStatus.Pending],
            ContractName = target.ContractName,
            Topic = target.Topic,
            CorrelationId = target.CorrelationId,
            CausationId = target.CausationId,
            TenantId = target.TenantId,
            CreatedAfter = now.AddMinutes(-1),
            CreatedBefore = now.AddMinutes(1)
        };

        var page = await store.MessageQuery.QueryAsync(
            filter,
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var deleted = await store.PurgeStore.PurgeAsync(filter).ConfigureAwait(false);

        page.Items.Should().ContainSingle().Which.Id.Should().Be(target.Id);
        deleted.Should().Be(1);
    }

    /// <summary>
    ///     Verifies batch append resolves a duplicate idempotency key to the original row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_ShouldReturnExistingRowForDuplicateIdempotencyKey()
    {
        var store = CreateStore();
        var now = BaseTime;
        var original = CreatePendingEnvelope(Guid.NewGuid(), now) with
        {
            TenantId = "tenant-batch",
            IdempotencyKey = "batch-key"
        };
        var first = await store.Writer.EnqueueAsync(original).ConfigureAwait(false);

        var batch = await store.Writer.AddBatchAsync([
            original with { Id = Guid.NewGuid(), Payload = "{\"changed\":true}" },
            CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(1)) with
            {
                TenantId = "tenant-batch",
                IdempotencyKey = "batch-key-2"
            }
        ]).ConfigureAwait(false);

        batch.Should().HaveCount(2);
        batch[0].Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        batch[0].Envelope.Id.Should().Be(first.Envelope.Id);
        batch[0].Envelope.Payload.Should().Be(first.Envelope.Payload);
        batch[1].Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
    }

    /// <summary>
    ///     Verifies strict batch replay rejects changed content for an existing message identifier.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_WithStrictChangedReplay_ShouldThrow()
    {
        var store = CreateStore();
        var original = CreatePendingEnvelope(Guid.NewGuid(), BaseTime);
        await store.Writer.EnqueueAsync(original).ConfigureAwait(false);

        var action = () => store.Writer.AddBatchAsync([
            original with
            {
                Payload = "{\"changed\":true}",
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            }
        ]);

        await action.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
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
    ///     Waits until a database clock has passed a visibility timestamp with a scheduling margin.
    /// </summary>
    /// <param name="visibleAfter">The timestamp that must be in the past before the method returns.</param>
    /// <returns>A task that represents the asynchronous wait.</returns>
    private static async Task WaitUntilVisibleAsync(DateTimeOffset visibleAfter)
    {
        var delay = visibleAfter - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(250);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies that a relational store uses its database clock instead of a skewed caller timestamp.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    protected async Task AssertDatabaseClockIgnoresCallerSkewAsync()
    {
        var store = CreateStore();
        var futureId = Guid.NewGuid();
        var readyId = Guid.NewGuid();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);
        var skewedNow = DateTimeOffset.UtcNow.AddYears(10);

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(futureId, BaseTime) with
        {
            VisibleAfter = visibleAfter
        }).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(readyId, BaseTime.AddSeconds(1))).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "skewed-publisher",
            Now = skewedNow,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(readyId);
        leased[0].LeaseExpiresAt.Should().NotBeNull();
        leased[0].LeaseExpiresAt.Should().BeBefore(skewedNow.AddYears(-1));
    }

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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(firstId, now) with { ContractName = "tests.events.a", Topic = "topic.a" }).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)) with { ContractName = "tests.events.b", Topic = "topic.b" }).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(3)) with { ContractName = "tests.events.a", Topic = "topic.a" }).ConfigureAwait(false);

        var pendingPage = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { Statuses = [OutboxStatus.Pending] },
            new OutboxMessagePageRequest { PageSize = 1 }).ConfigureAwait(false);

        pendingPage.Items.Should().ContainSingle();
        pendingPage.Items[0].Id.Should().Be(secondId);
        pendingPage.HasMore.Should().BeTrue();

        var topicPage = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter { Topic = "topic.a" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

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

        await store.Writer.EnqueueAsync(CreatePendingEnvelope(pendingId, now)).ConfigureAwait(false);
        await store.Writer.EnqueueAsync(CreatePendingEnvelope(publishedId, now.AddSeconds(1))).ConfigureAwait(false);

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.StateWriter.PersistAsync([leased[0].AsPublished(DateTimeOffset.UtcNow)]).ConfigureAwait(false);

        var deleted = await store.PurgeStore.PurgeAsync(new OutboxMessageFilter
        {
            Statuses = [OutboxStatus.Published]
        }).ConfigureAwait(false);

        deleted.Should().Be(1);

        var remaining = await store.MessageQuery.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        remaining.Items.Should().ContainSingle();
        remaining.Items[0].Id.Should().Be(publishedId);
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
}
