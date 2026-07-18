using System.Text.Json;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared inbox store contract tests for in-memory, PostgreSQL, and EF Core implementations.
/// </summary>
public abstract class InboxStoreContractTests
{
    /// <summary>
    ///     Gets the UTC timestamp used as the baseline for lease and visibility assertions.
    /// </summary>
    protected static DateTimeOffset BaseTime { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>
    ///     Creates a fresh store instance for one test.
    /// </summary>
    /// <returns>The writer, lease, terminal, retention, and diagnostics roles backed by the same store instance.</returns>
    protected abstract InboxStoreRoles CreateStore();

    /// <summary>
    ///     Verifies invalid lease inputs are rejected before a store can create an unusable lease.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenRequestIsInvalid_ShouldRejectRequest()
    {
        var roles = CreateStore();
        var request = new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        };

        var zeroBatch = () => roles.LeaseStore.LeasePendingAsync(request with { BatchSize = 0 });
        var blankOwner = () => roles.LeaseStore.LeasePendingAsync(request with { LeaseOwner = " " });
        var negativeDuration = () => roles.LeaseStore.LeasePendingAsync(
            request with { LeaseDuration = TimeSpan.FromSeconds(-1) });

        await zeroBatch.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await blankOwner.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        await negativeDuration.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that cancellation requested before an append prevents any store mutation.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenCancellationIsRequested_ShouldNotStoreCommand()
    {
        var roles = CreateStore();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var append = () => roles.Writer.EnqueueAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.cancelled",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }, cancellationSource.Token);

        await append.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var counts = await roles.DiagnosticsStore.GetStatusCountsAsync().ConfigureAwait(false);
        counts.Values.Sum().Should().Be(0);
    }

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

        var firstResult = await roles.Writer.EnqueueAsync(new InboxEnvelope
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

        var first = firstResult.Envelope;
        var duplicate = await roles.Writer.EnqueueAsync(first with
        {
            Id = secondCommandId,
            Payload = "{\"orderId\":\"2\"}"
        });

        firstResult.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        duplicate.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        duplicate.Envelope.Id.Should().Be(first.Id);
        duplicate.Envelope.Payload.Should().Be(first.Payload);
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

        tenantA.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        tenantB.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        tenantA.Envelope.Id.Should().NotBe(tenantB.Envelope.Id);
        JsonDocument.Parse(tenantA.Envelope.Payload).RootElement.GetProperty("tenant").GetString().Should().Be("a");
        JsonDocument.Parse(tenantB.Envelope.Payload).RootElement.GetProperty("tenant").GetString().Should().Be("b");
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

        var firstResult = await roles.Writer.EnqueueAsync(new InboxEnvelope
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

        var first = firstResult.Envelope;
        var duplicate = await roles.Writer.EnqueueAsync(first with
        {
            Id = Guid.NewGuid(),
            Payload = """{"n":2}"""
        });

        firstResult.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        duplicate.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        duplicate.Envelope.Id.Should().Be(first.Id);
        duplicate.Envelope.Payload.Should().Be(first.Payload);
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

        var firstResult = await roles.Writer.EnqueueAsync(
            CreatePendingEnvelope(commandId, now) with { IdempotencyKey = null });
        var duplicate = await roles.Writer.EnqueueAsync(
            firstResult.Envelope with { Payload = "{\"changed\":true}" });

        firstResult.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        duplicate.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        duplicate.Envelope.Id.Should().Be(commandId);
        duplicate.Envelope.Payload.Should().Be(firstResult.Envelope.Payload);
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

        stored.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        stored.Envelope.VisibleAfter.Should().Be(visibleAfter);
        stored.Envelope.CorrelationId.Should().Be("correlation-1");
        stored.Envelope.CausationId.Should().Be("causation-1");
        stored.Envelope.TenantId.Should().Be("tenant-1");
        using var traceDocument = JsonDocument.Parse(stored.Envelope.TraceContext!);
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

        var stored = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter { MessageId = commandId },
            new InboxMessagePageRequest { PageSize = 1 });

        stored.Items.Should().ContainSingle();
        stored.Items[0].Status.Should().Be(InboxStatus.Failed);
        stored.Items[0].VisibleAfter.Should().Be(visibleAfter);
        stored.Items[0].LastError.Should().Be("transient failure");
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
        var visibleAfter = DateTimeOffset.UtcNow.AddMilliseconds(500);

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var firstLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateWriter.PersistAsync([firstLease[0].AsFailed("retry me", visibleAfter)]);

        await WaitUntilVisibleAsync(visibleAfter).ConfigureAwait(false);

        var visible = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = DateTimeOffset.UtcNow,
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
        var visibleAfter = DateTimeOffset.UtcNow.AddMilliseconds(500);

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

        await roles.StateWriter.PersistAsync([firstLease[0].AsFailed("retry", visibleAfter)]);

        await WaitUntilVisibleAsync(visibleAfter).ConfigureAwait(false);

        var secondLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = DateTimeOffset.UtcNow,
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
        var leaseDuration = TimeSpan.FromMilliseconds(200);

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));

        var staleLease = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker",
            Now = now,
            LeaseDuration = leaseDuration
        });

        staleLease.Should().ContainSingle();
        await Task.Delay(leaseDuration.Add(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

        var reclaimed = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker",
            Now = DateTimeOffset.UtcNow,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        reclaimed.Should().ContainSingle();
        reclaimed[0].Id.Should().Be(commandId);
        reclaimed[0].LeaseOwner.Should().Be("worker");
        reclaimed[0].AttemptCount.Should().Be(2);
        reclaimed[0].LeaseGeneration.Should().Be(staleLease[0].LeaseGeneration + 1);

        var stalePersist = await roles.StateWriter.PersistAsync([staleLease[0].AsCompleted()]);
        stalePersist.AppliedCount.Should().Be(0);

        var currentPersist = await roles.StateWriter.PersistAsync([reclaimed[0].AsCompleted()]);
        currentPersist.AppliedCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that only the active inbox lease owner can renew a lease.
    /// </summary>
    [Fact]
    public async Task RenewLeaseAsync_ShouldRequireActiveLeaseOwner()
    {
        var roles = CreateStore();
        var now = BaseTime;
        var commandId = Guid.NewGuid();

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(commandId, now));
        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-a",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        var renewed = await roles.LeaseStore.RenewLeaseAsync(
            new LeaseRenewalRequest(
                commandId,
                "worker-a",
                leased[0].LeaseGeneration,
                TimeSpan.FromMinutes(1),
                now.AddMinutes(2)));
        var rejected = await roles.LeaseStore.RenewLeaseAsync(
            new LeaseRenewalRequest(
                commandId,
                "worker-b",
                leased[0].LeaseGeneration,
                TimeSpan.FromMinutes(2),
                now.AddMinutes(3)));
        var staleGeneration = await roles.LeaseStore.RenewLeaseAsync(
            new LeaseRenewalRequest(
                commandId,
                "worker-a",
                leased[0].LeaseGeneration - 1,
                TimeSpan.FromMinutes(3),
                now.AddMinutes(4)));

        renewed.Should().BeTrue();
        rejected.Should().BeFalse();
        staleGeneration.Should().BeFalse();
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
        await Task.WhenAll(firstBatchTask, secondBatchTask).ConfigureAwait(false);
        var firstBatch = await firstBatchTask.ConfigureAwait(false);
        var secondBatch = await secondBatchTask.ConfigureAwait(false);

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
        stored[0].Envelope.Id.Should().Be(firstId);
        stored[1].Envelope.Id.Should().Be(secondId);
        stored.Select(result => result.Outcome).Should().OnlyContain(outcome => outcome == InboxAcceptOutcome.Accepted);
    }

    /// <summary>
    ///     Verifies that a repeated message identifier within one batch reports the later slot as an existing row.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_RepeatedMessageIdWithinBatch_ShouldReportExistingOutcome()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var first = CreatePendingEnvelope(commandId, BaseTime) with { IdempotencyKey = null };

        var results = await roles.Writer.AddBatchAsync([
            first,
            first with { Payload = "{\"changed\":true}" }
        ]);

        results.Should().HaveCount(2);
        results[0].Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        results[1].Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        results[1].Envelope.Payload.Should().Be(first.Payload);
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

        var firstResult = await roles.Writer.EnqueueAsync(new InboxEnvelope
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

        var first = firstResult.Envelope;
        var batch = await roles.Writer.AddBatchAsync([
            first with { Id = duplicateId, Payload = "{\"orderId\":\"changed\"}" },
            CreatePendingEnvelope(Guid.NewGuid(), now) with { IdempotencyKey = "batch-ship-2" }
        ]);

        batch.Should().HaveCount(2);
        batch[0].Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        batch[0].Envelope.Id.Should().Be(first.Id);
        batch[0].Envelope.Payload.Should().Be(first.Payload);
        batch[1].Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        batch[1].Envelope.IdempotencyKey.Should().Be("batch-ship-2");
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
            first.Envelope with { Payload = "{\"changed\":true}" }
        ]);

        batch.Should().ContainSingle();
        batch[0].Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        batch[0].Envelope.Id.Should().Be(commandId);
        batch[0].Envelope.Payload.Should().Be(first.Envelope.Payload);
    }

    /// <summary>
    ///     Verifies that an empty batch is a successful no-op for every batch-oriented store role.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EmptyBatchOperations_ShouldReturnEmptyResults()
    {
        var roles = CreateStore();

        var appended = await roles.Writer.AddBatchAsync([]).ConfigureAwait(false);
        var persisted = await roles.StateWriter.PersistAsync([]).ConfigureAwait(false);
        var requeued = await roles.DeadLetterStore.RequeueAsync([]).ConfigureAwait(false);

        appended.Should().BeEmpty();
        persisted.AppliedCount.Should().Be(0);
        persisted.SkippedCount.Should().Be(0);
        requeued.Requested.Should().Be(0);
        requeued.Requeued.Should().Be(0);
    }

    /// <summary>
    ///     Verifies one persist batch can apply multiple rows for every terminal inbox status.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PersistAsync_WithMixedTerminalBatch_ShouldApplyEveryTransition()
    {
        var roles = CreateStore();
        var now = BaseTime;
        var source = Enumerable.Range(0, 6)
            .Select(index => CreatePendingEnvelope(Guid.NewGuid(), now.AddMilliseconds(index)))
            .ToArray();
        await roles.Writer.AddBatchAsync(source).ConfigureAwait(false);

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = source.Length,
            LeaseOwner = "mixed-worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        }).ConfigureAwait(false);

        leased.Should().HaveCount(source.Length);
        var terminal = new[]
        {
            leased[0].AsCompleted(),
            leased[1].AsCompleted(),
            leased[2].AsFailed("temporary-1", now.AddMinutes(5)),
            leased[3].AsFailed("temporary-2", now.AddMinutes(5)),
            leased[4].AsDeadLettered("poison-1"),
            leased[5].AsDeadLettered("poison-2")
        };

        var result = await roles.StateWriter.PersistAsync(terminal).ConfigureAwait(false);
        var page = await roles.MessageQuery.QueryAsync(
            new InboxMessageFilter { MessageIds = source.Select(envelope => envelope.Id).ToArray() },
            new InboxMessagePageRequest { PageSize = source.Length }).ConfigureAwait(false);

        result.AppliedCount.Should().Be(source.Length);
        result.SkippedCount.Should().Be(0);
        page.Items.Should().HaveCount(source.Length);
        page.Items.Count(envelope => envelope.Status == InboxStatus.Completed).Should().Be(2);
        page.Items.Count(envelope => envelope.Status == InboxStatus.Failed).Should().Be(2);
        page.Items.Count(envelope => envelope.Status == InboxStatus.DeadLettered).Should().Be(2);
    }

    /// <summary>
    ///     Verifies every inbox message predicate composes consistently for query and purge operations.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CompleteFilter_ShouldMatchOnlyTheIntendedMessage()
    {
        var roles = CreateStore();
        var now = BaseTime;
        var target = CreatePendingEnvelope(Guid.NewGuid(), now) with
        {
            ContractName = "tests.commands.filtered",
            CorrelationId = "correlation-filtered",
            CausationId = "causation-filtered",
            TenantId = "tenant-filtered",
            IdempotencyKey = "tenant-filtered-key"
        };
        var other = CreatePendingEnvelope(Guid.NewGuid(), now.AddMinutes(5)) with
        {
            ContractName = "tests.commands.other",
            CorrelationId = "correlation-other",
            CausationId = "causation-other",
            TenantId = "tenant-other",
            IdempotencyKey = "tenant-other-key"
        };
        await roles.Writer.AddBatchAsync([target, other]).ConfigureAwait(false);

        var filter = new InboxMessageFilter
        {
            MessageId = target.Id,
            MessageIds = [target.Id, other.Id],
            Statuses = [InboxStatus.Pending],
            ContractName = target.ContractName,
            CorrelationId = target.CorrelationId,
            CausationId = target.CausationId,
            TenantId = target.TenantId,
            CreatedAfter = now.AddMinutes(-1),
            CreatedBefore = now.AddMinutes(1)
        };

        var page = await roles.MessageQuery.QueryAsync(
            filter,
            new InboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);
        var deleted = await roles.PurgeStore.PurgeAsync(filter).ConfigureAwait(false);

        page.Items.Should().ContainSingle().Which.Id.Should().Be(target.Id);
        deleted.Should().Be(1);
    }

    /// <summary>
    ///     Verifies strict batch replay rejects changed content for an existing message identifier.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AddBatchAsync_WithStrictChangedReplay_ShouldThrow()
    {
        var roles = CreateStore();
        var original = CreatePendingEnvelope(Guid.NewGuid(), BaseTime);
        await roles.Writer.EnqueueAsync(original).ConfigureAwait(false);

        var action = () => roles.Writer.AddBatchAsync([
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
        var roles = CreateStore();
        var futureId = Guid.NewGuid();
        var readyId = Guid.NewGuid();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);
        var skewedNow = DateTimeOffset.UtcNow.AddYears(10);

        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(futureId, BaseTime) with
        {
            VisibleAfter = visibleAfter
        }).ConfigureAwait(false);
        await roles.Writer.EnqueueAsync(CreatePendingEnvelope(readyId, BaseTime.AddSeconds(1))).ConfigureAwait(false);

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "skewed-worker",
            Now = skewedNow,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(readyId);
        leased[0].LeaseExpiresAt.Should().NotBeNull();
        leased[0].LeaseExpiresAt.Should().BeBefore(skewedNow.AddYears(-1));
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
