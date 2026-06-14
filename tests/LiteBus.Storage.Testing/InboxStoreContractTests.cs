using System.Text.Json;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared inbox store contract tests for in-memory, PostgreSQL, and EF Core implementations.
/// </summary>
public abstract class InboxStoreContractTests
{
    /// <summary>
    ///     Gets a fixed UTC timestamp used as the baseline for lease and visibility assertions.
    /// </summary>
    protected static DateTimeOffset BaseTime { get; } = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Creates a fresh store instance for one test.
    /// </summary>
    /// <returns>The writer, lease, terminal, retention, and diagnostics roles backed by the same store instance.</returns>
    protected abstract InboxStoreRoles CreateStore();

    /// <summary>
    ///     Verifies that duplicate idempotency keys return the original stored command.
    /// </summary>
    [Fact]
    public async Task AddAsync_ShouldReturnExistingCommandForDuplicateIdempotencyKey()
    {
        var roles = CreateStore();
        var firstCommandId = Guid.NewGuid();
        var secondCommandId = Guid.NewGuid();
        var now = BaseTime;

        var first = await roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = firstCommandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = "ship-1"
        });

        var duplicate = await roles.Writer.EnqueueAsync(first with
        {
            Id = secondCommandId,
            Payload = "{\"orderId\":\"2\"}"
        });

        duplicate.Id.Should().Be(first.Id);
        duplicate.Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Verifies that the same idempotency key persists independently for different tenants.
    /// </summary>
    [Fact]
    public async Task AddAsync_SameIdempotencyKeyDifferentTenants_ShouldPersistBoth()
    {
        var roles = CreateStore();
        var now = BaseTime;
        const string idempotencyKey = "ship-shared";

        var tenantA = await roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = """{"tenant":"a"}""",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            TenantId = "tenant-a",
            IdempotencyKey = idempotencyKey
        });

        var tenantB = await roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = """{"tenant":"b"}""",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            TenantId = "tenant-b",
            IdempotencyKey = idempotencyKey
        });

        tenantA.Id.Should().NotBe(tenantB.Id);
        JsonDocument.Parse(tenantA.Payload).RootElement.GetProperty("tenant").GetString().Should().Be("a");
        JsonDocument.Parse(tenantB.Payload).RootElement.GetProperty("tenant").GetString().Should().Be("b");
    }

    /// <summary>
    ///     Verifies that duplicate idempotency keys within one tenant return the original stored command.
    /// </summary>
    [Fact]
    public async Task AddAsync_SameTenantSameIdempotencyKey_ShouldDedup()
    {
        var roles = CreateStore();
        var now = BaseTime;
        const string tenantId = "tenant-a";
        const string idempotencyKey = "ship-tenant-a";

        var first = await roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = """{"n":1}""",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey
        });

        var duplicate = await roles.Writer.EnqueueAsync(first with
        {
            Id = Guid.NewGuid(),
            Payload = """{"n":2}"""
        });

        duplicate.Id.Should().Be(first.Id);
        duplicate.Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Verifies that a tenant-scoped lease request does not claim rows owned by another tenant.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenTenantFilterDoesNotMatchStoredTenant_ShouldNotLeaseRow()
    {
        var roles = CreateStore();
        var now = BaseTime;
        var tenantBCommandId = Guid.NewGuid();

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(tenantBCommandId, now) with
        {
            TenantId = "tenant-b",
            IdempotencyKey = "tenant-b-ship"
        });

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-a",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            TenantId = "tenant-a"
        });

        leased.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a tenant-scoped message query excludes rows stored for another tenant.
    /// </summary>
    [Fact]
    public async Task QueryAsync_WhenTenantFilterDoesNotMatchStoredTenant_ShouldExcludeOtherTenantRows()
    {
        var roles = CreateStore();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now) with
        {
            TenantId = "tenant-a",
            IdempotencyKey = "tenant-a-query"
        });

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(1)) with
        {
            TenantId = "tenant-b",
            IdempotencyKey = "tenant-b-query"
        });

        var tenantAPage = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter { TenantId = "tenant-a" },
            new InboxMessagePageRequest { PageSize = 10 });

        tenantAPage.Items.Should().ContainSingle();
        tenantAPage.Items[0].TenantId.Should().Be("tenant-a");
    }

    /// <summary>
    ///     Verifies that duplicate command identifiers return the original stored row.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenCommandIdAlreadyExists_ShouldReturnExistingRow()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        var first = await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now) with { IdempotencyKey = null });
        var duplicate = await roles.Writer.EnqueueAsync(first with { Payload = "{\"changed\":true}" });

        duplicate.Id.Should().Be(commandId);
        duplicate.Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Verifies that trace metadata and delayed visibility are preserved on append.
    /// </summary>
    [Fact]
    public async Task AddAsync_ShouldPersistMetadataAndVisibleAfter()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var visibleAfter = BaseTime.AddHours(2);

        var stored = await roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = BaseTime,
            VisibleAfter = visibleAfter,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1",
            TraceContext = "{\"traceparent\":\"00-abc\"}"
        });

        stored.VisibleAfter.Should().Be(visibleAfter);
        stored.CorrelationId.Should().Be("correlation-1");
        stored.CausationId.Should().Be("causation-1");
        stored.TenantId.Should().Be("tenant-1");
        using var traceDocument = JsonDocument.Parse(stored.TraceContext!);
        traceDocument.RootElement.GetProperty("traceparent").GetString().Should().Be("00-abc");
    }

    /// <summary>
    ///     Verifies that leasing, completion, and re-leasing behave as expected.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldLeaseAndCompleteCommand()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(commandId);
        leased[0].Status.Should().Be(InboxStatus.Processing);
        leased[0].AttemptCount.Should().Be(1);
        leased[0].LeaseOwner.Should().Be("worker-1");

        await roles.StateWriter.PersistAsync([leased[0].AsCompleted()]);

        var afterCompletion = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        afterCompletion.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that commands with a future visible-after timestamp are not leased early.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now) with
        {
            VisibleAfter = now.AddHours(1)
        });

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-1",
            Now = now.AddMinutes(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that leasing orders by created time and respects the batch size.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldOrderByCreatedAtAndRespectBatchSize()
    {
        var roles = CreateStore();
        var now = BaseTime;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(firstId, now));
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)));
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2)));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "worker-1",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        });

        leased.Should().HaveCount(2);
        leased[0].Id.Should().Be(firstId);
        leased[1].Id.Should().Be(secondId);
    }

    /// <summary>
    ///     Verifies that mark-failed records retry visibility and diagnostic text.
    /// </summary>
    [Fact]
    public async Task MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;
        var visibleAfter = now.AddMinutes(10);

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([leased[0].AsFailed("transient failure", visibleAfter)]);

        var hidden = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = visibleAfter,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].Status.Should().Be(InboxStatus.Processing);
        visible[0].LastError.Should().Be("transient failure");
    }

    /// <summary>
    ///     Verifies that failed commands become leasable again once visible-after is reached.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenFailedAndVisibleAfterReached_ShouldLeaseAgain()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var firstLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([firstLease[0].AsFailed("retry me", now.AddMinutes(5))]);

        var hidden = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);
        visible[0].Status.Should().Be(InboxStatus.Processing);
    }

    /// <summary>
    ///     Verifies that dead-lettered commands are not leased again.
    /// </summary>
    [Fact]
    public async Task MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([leased[0].AsDeadLettered("exhausted retries")]);

        var afterDeadLetter = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        afterDeadLetter.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that attempt count increments when a message is leased.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldIncrementAttemptCountAtLeaseTime()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var firstLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        firstLease.Should().ContainSingle();
        firstLease[0].AttemptCount.Should().Be(1);
        firstLease[0].LeaseExpiresAt.Should().NotBeNull();

        await roles.StateWriter.PersistAsync([firstLease[0].AsFailed("retry", now.AddMinutes(5))]);

        var secondLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        secondLease.Should().ContainSingle();
        secondLease[0].AttemptCount.Should().Be(2);
    }

    /// <summary>
    ///     Verifies that leased processing rows always carry a non-null lease expiry timestamp.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldSetLeaseExpiresAtWhenProcessing()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;
        var leaseDuration = TimeSpan.FromMinutes(2);

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = leaseDuration
        });

        leased.Should().ContainSingle();
        leased[0].LeaseExpiresAt.Should().NotBeNull();
        leased[0].LeaseExpiresAt.Should().BeOnOrAfter(now.Add(leaseDuration));
    }

    /// <summary>
    ///     Verifies that trace context written on accept is returned on leased envelopes.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldRoundTripTraceContext()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;
        const string traceContext = """{"traceparent":"00-abc-def"}""";

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now) with
        {
            TraceContext = traceContext
        });

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].TraceContext.Should().Be(traceContext);
    }

    /// <summary>
    ///     Verifies that expired processing leases can be reclaimed by another worker.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenLeaseExpires_ShouldReclaimProcessingCommand()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = now,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        var reclaimed = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fresh-worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        reclaimed.Should().ContainSingle();
        reclaimed[0].Id.Should().Be(commandId);
        reclaimed[0].LeaseOwner.Should().Be("fresh-worker");
        reclaimed[0].AttemptCount.Should().Be(2);
    }

    /// <summary>
    ///     Verifies that concurrent lease calls claim disjoint commands when the store supports it.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ConcurrentWorkers_ShouldLeaseDisjointCommands()
    {
        var roles = CreateStore();
        var now = BaseTime;

        for (var index = 0; index < 8; index++)
        {
            await roles.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index)));
        }

        var request = new InboxLeaseRequest
        {
            BatchSize = 4,
            LeaseOwner = "worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        var firstBatchTask = roles.LeaseStore.LeasePendingAsync(request with { LeaseOwner = "worker-a" });
        var secondBatchTask = roles.LeaseStore.LeasePendingAsync(request with { LeaseOwner = "worker-b" });
        await Task.WhenAll(firstBatchTask, secondBatchTask);
        var firstBatch = await firstBatchTask;
        var secondBatch = await secondBatchTask;

        var leasedIds = firstBatch.Select(command => command.Id)
            .Concat(secondBatch.Select(command => command.Id))
            .ToArray();

        leasedIds.Should().HaveCount(8);
        leasedIds.Should().OnlyHaveUniqueItems();
        firstBatch.Should().OnlyContain(command => command.LeaseOwner == "worker-a");
        secondBatch.Should().OnlyContain(command => command.LeaseOwner == "worker-b");
    }

    /// <summary>
    ///     Verifies that status counts reflect stored envelopes grouped by status.
    /// </summary>
    [Fact]
    public async Task GetStatusCountsAsync_ShouldGroupByStatus()
    {
        var roles = CreateStore();
        var pendingId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(pendingId, now));
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(completedId, now.AddSeconds(1)));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([leased[0].AsCompleted()]);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync();

        counts[InboxStatus.Pending].Should().Be(1);
        counts[InboxStatus.Completed].Should().Be(1);
    }

    /// <summary>
    ///     Verifies that dead-letter replay returns envelopes to the pending queue.
    /// </summary>
    [Fact]
    public async Task RequeueDeadLetterAsync_ShouldReturnEnvelopeToPending()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([leased[0].AsDeadLettered("manual replay")]);

        await roles.DeadLetterStore.RequeueAsync(commandId);

        var requeued = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        requeued.Should().ContainSingle();
        requeued[0].Id.Should().Be(commandId);
        requeued[0].Status.Should().Be(InboxStatus.Processing);
    }

    /// <summary>
    ///     Verifies that the string message id overload parses GUID identifiers for bulk replay.
    /// </summary>
    [Fact]
    public async Task RequeueDeadLetterAsync_WithStringIds_ShouldRequeueMatchingRows()
    {
        var roles = CreateStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = BaseTime;

        foreach (var commandId in new[] { firstId, secondId })
        {
            await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

            var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 1,
                LeaseOwner = "worker-1",
                Now = now,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

            await roles.StateWriter.PersistAsync([leased[0].AsDeadLettered("bulk replay")]);
        }

        await roles.DeadLetterStore.RequeueAsync(
            [firstId.ToString("D"), secondId.ToString("D")]);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync();
        counts.Should().NotContainKey(InboxStatus.DeadLettered);
        counts[InboxStatus.Pending].Should().Be(2);
    }

    /// <summary>
    ///     Verifies that message queries filter by status and support keyset pagination.
    /// </summary>
    [Fact]
    public async Task QueryAsync_ShouldFilterAndPageByCreatedAt()
    {
        var roles = CreateStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(firstId, now) with { ContractName = "tests.commands.a" });
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)) with { ContractName = "tests.commands.b" });

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([leased[0].AsCompleted()]);
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(3)) with { ContractName = "tests.commands.a" });

        var pendingPage = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter { Statuses = [InboxStatus.Pending] },
            new InboxMessagePageRequest { PageSize = 1 });

        pendingPage.Items.Should().ContainSingle();
        pendingPage.Items[0].Id.Should().Be(secondId);
        pendingPage.HasMore.Should().BeTrue();
        pendingPage.NextCursor.Should().NotBeNullOrWhiteSpace();

        var nextPendingPage = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter { Statuses = [InboxStatus.Pending] },
            new InboxMessagePageRequest { PageSize = 1, Cursor = pendingPage.NextCursor });

        nextPendingPage.Items.Should().ContainSingle();
        nextPendingPage.Items[0].Id.Should().Be(thirdId);
        nextPendingPage.HasMore.Should().BeFalse();

        var contractPage = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter { ContractName = "tests.commands.a" },
            new InboxMessagePageRequest { PageSize = 10 });

        contractPage.Items.Should().HaveCount(2);
        contractPage.Items.Select(envelope => envelope.Id).Should().BeEquivalentTo([firstId, thirdId]);
    }

    /// <summary>
    ///     Verifies that purge deletes only rows that match the supplied filter.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_ShouldDeleteMatchingRows()
    {
        var roles = CreateStore();
        var pendingId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(pendingId, now));
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(completedId, now.AddSeconds(1)));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([leased[0].AsCompleted()]);

        var deleted = await roles.PurgeStore.PurgeAsync(new InboxMessageFilter
        {
            Statuses = [InboxStatus.Completed]
        });

        deleted.Should().Be(1);

        var remaining = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = 10 });

        remaining.Items.Should().ContainSingle();
        remaining.Items[0].Id.Should().Be(completedId);
    }

    /// <summary>
    ///     Verifies that batch accept returns stored rows in input order.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_ShouldReturnStoredEnvelopesInInputOrder()
    {
        var roles = CreateStore();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = BaseTime;

        var stored = await roles.Writer.AddBatchAsync([
            CreatePendingEnvelope(firstId, now),
            CreatePendingEnvelope(secondId, now) with { ContractName = "tests.commands.archive" }
        ]);

        stored.Should().HaveCount(2);
        stored[0].Id.Should().Be(firstId);
        stored[1].Id.Should().Be(secondId);
    }

    /// <summary>
    ///     Verifies that batch accept returns the original row when an idempotency key conflicts.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_ShouldReturnExistingRowForDuplicateIdempotencyKey()
    {
        var roles = CreateStore();
        var firstId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();
        var now = BaseTime;

        var first = await roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = firstId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = "batch-ship-1"
        });

        var batch = await roles.Writer.AddBatchAsync([
            first with { Id = duplicateId, Payload = "{\"orderId\":\"changed\"}" },
            CreatePendingEnvelope(Guid.NewGuid(), now) with { IdempotencyKey = "batch-ship-2" }
        ]);

        batch.Should().HaveCount(2);
        batch[0].Id.Should().Be(first.Id);
        batch[0].Payload.Should().Be(first.Payload);
        batch[1].IdempotencyKey.Should().Be("batch-ship-2");
    }

    /// <summary>
    ///     Verifies that batch accept returns the original row when a message identifier already exists.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_WhenMessageIdAlreadyExists_ShouldReturnExistingRow()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        var first = await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now) with { IdempotencyKey = null });

        var batch = await roles.Writer.AddBatchAsync([
            first with { Payload = "{\"changed\":true}" }
        ]);

        batch.Should().ContainSingle();
        batch[0].Id.Should().Be(commandId);
        batch[0].Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Creates a pending envelope for contract tests.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="createdAt">The storage timestamp.</param>
    /// <returns>A pending envelope ready for append.</returns>
    protected static InboxEnvelope CreatePendingEnvelope(Guid commandId, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };
    }

    /// <summary>
    ///     Holds the inbox store roles implemented by one persistence backend.
    /// </summary>
    /// <param name="Writer">The append-only writer role.</param>
    /// <param name="LeaseStore">The lease role used by the processor.</param>
    /// <param name="StateWriter">The state writer role used by the processor.</param>
    /// <param name="DeadLetterStore">The dead-letter replay role.</param>
    /// <param name="RetentionStore">The retention role used by cleanup.</param>
    /// <param name="DiagnosticsStore">The diagnostics role used by operators.</param>
    /// <param name="MessageQuery">The message query role used by browse APIs.</param>
    /// <param name="PurgeStore">The purge role used by operator cleanup.</param>
    public sealed record InboxStoreRoles(
        IInboxStore Writer,
        IInboxLeaseStore LeaseStore,
        IInboxStateWriter StateWriter,
        IInboxDeadLetterStore DeadLetterStore,
        IInboxRetentionStore RetentionStore,
        IInboxDiagnosticsStore DiagnosticsStore,
        IInboxMessageQuery MessageQuery,
        IInboxPurgeStore PurgeStore);
}