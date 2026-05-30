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

        await store.Writer.AddAsync(CreatePendingEnvelope(messageId, now));

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

        await store.State.MarkFailedAsync(new OutboxEnvelopeFailure
        {
            Id = messageId,
            Error = "publisher unavailable",
            VisibleAfter = now.AddMinutes(5)
        });

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

        await store.State.MarkPublishedAsync(messageId);
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

        var first = await store.Writer.AddAsync(CreatePendingEnvelope(messageId, now));
        var duplicate = await store.Writer.AddAsync(first with { Payload = "{\"orderId\":\"2\"}" });

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

        var stored = await store.Writer.AddAsync(new OutboxEnvelope
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
            TenantId = "tenant-1"
        });

        stored.Topic.Should().Be("orders");
        stored.VisibleAfter.Should().Be(visibleAfter);
        stored.CorrelationId.Should().Be("correlation-1");
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

        await store.Writer.AddAsync(CreatePendingEnvelope(messageId, now) with
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

        await store.Writer.AddAsync(CreatePendingEnvelope(firstId, now));
        await store.Writer.AddAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)));
        await store.Writer.AddAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2)));

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

        await store.Writer.AddAsync(CreatePendingEnvelope(messageId, now));

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();

        await store.State.MarkFailedAsync(new OutboxEnvelopeFailure
        {
            Id = messageId,
            Error = "broker down",
            VisibleAfter = visibleAfter
        });

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

        await store.Writer.AddAsync(CreatePendingEnvelope(messageId, now));

        await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.State.MoveToDeadLetterAsync(new OutboxEnvelopeDeadLetter
        {
            Id = messageId,
            Reason = "poison message"
        });

        var leased = await store.Lease.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
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

        await store.Writer.AddAsync(CreatePendingEnvelope(messageId, now));

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
            await store.Writer.AddAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index)));
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
    ///     Holds the three outbox store roles exercised by contract tests.
    /// </summary>
    /// <param name="Writer">The writer role.</param>
    /// <param name="Lease">The lease role.</param>
    /// <param name="State">The state role.</param>
    public sealed record OutboxStoreContracts(
        IOutboxStore Writer,
        IOutboxLeaseStore Lease,
        IOutboxStateStore State);
}
