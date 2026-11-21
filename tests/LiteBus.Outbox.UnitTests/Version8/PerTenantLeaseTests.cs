using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox.UnitTests.Version8;

/// <summary>
///     Verifies one lease call returns at most the configured envelopes of one tenant.
/// </summary>
public sealed class PerTenantLeaseTests
{
    /// <summary>
    ///     Verifies a capped lease limits each tenant while filling the batch from other tenants.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_when_cap_set_should_limit_each_tenant()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(store, now, "tenant-a", 3, "tenant-b", 2).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            BatchSize = 10,
            LeaseOwner = "worker",
            MaxPerTenant = 2
        }).ConfigureAwait(false);

        leased.Should().HaveCount(4);
        leased.Count(envelope => envelope.TenantId == "tenant-a").Should().Be(2);
        leased.Count(envelope => envelope.TenantId == "tenant-b").Should().Be(2);
    }

    /// <summary>
    ///     Verifies unscoped envelopes count as one tenant under the cap.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_when_cap_set_should_count_unscoped_as_one_tenant()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(store, now, null, 3, "tenant-a", 1).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            BatchSize = 10,
            LeaseOwner = "worker",
            MaxPerTenant = 2
        }).ConfigureAwait(false);

        leased.Should().HaveCount(3);
        leased.Count(envelope => string.IsNullOrEmpty(envelope.TenantId)).Should().Be(2);
        leased.Count(envelope => envelope.TenantId == "tenant-a").Should().Be(1);
    }

    /// <summary>
    ///     Verifies envelopes skipped by the cap stay pending for a later lease call.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_when_cap_skips_envelopes_should_lease_them_on_a_later_call()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(store, now, "tenant-a", 3).ConfigureAwait(false);

        var first = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            BatchSize = 10,
            LeaseOwner = "worker-one",
            MaxPerTenant = 2
        }).ConfigureAwait(false);

        first.Should().HaveCount(2);

        var second = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            BatchSize = 10,
            LeaseOwner = "worker-two",
            MaxPerTenant = 2
        }).ConfigureAwait(false);

        second.Should().ContainSingle();
        second[0].TenantId.Should().Be("tenant-a");
    }

    /// <summary>
    ///     Verifies a lease without a cap returns the full batch.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_when_cap_is_null_should_lease_full_batch()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(store, now, "tenant-a", 3, "tenant-b", 2).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1),
            BatchSize = 10,
            LeaseOwner = "worker"
        }).ConfigureAwait(false);

        leased.Should().HaveCount(5);
    }

    /// <summary>
    ///     Verifies the processor passes the configured per-tenant cap to the store.
    /// </summary>
    [Fact]
    public async Task ProcessorLease_when_options_cap_set_should_pass_it_to_the_store()
    {
        var leaseStore = new CapturingLeaseStore();
        var operations = new OutboxPipelinedMessageProcessorOperations(
            leaseStore,
            new NopStateWriter(),
            new NopDispatcher(),
            TimeProvider.System,
            [],
            NullLogger.Instance);

        await operations.LeasePendingAsync(
            "worker",
            new OutboxProcessorOptions { BatchSize = 10, MaxLeasedPerTenant = 2 },
            DateTimeOffset.UtcNow,
            CancellationToken.None).ConfigureAwait(false);

        leaseStore.Request.Should().NotBeNull();
        leaseStore.Request!.MaxPerTenant.Should().Be(2);
        leaseStore.Request.BatchSize.Should().Be(10);
    }

    private static async Task SeedAsync(InMemoryOutboxStore store, DateTimeOffset now, string? firstTenant, int firstCount, string? secondTenant = null, int secondCount = 0)
    {
        var createdAt = now;
        var envelopes = new List<OutboxEnvelope>(firstCount + secondCount);

        for (var index = 0; index < firstCount; index++)
        {
            envelopes.Add(PendingEnvelope(firstTenant, createdAt));
            createdAt = createdAt.AddSeconds(1);
        }

        for (var index = 0; index < secondCount; index++)
        {
            envelopes.Add(PendingEnvelope(secondTenant, createdAt));
            createdAt = createdAt.AddSeconds(1);
        }

        await store.AddBatchAsync(envelopes).ConfigureAwait(false);
    }

    private static OutboxEnvelope PendingEnvelope(string? tenantId, DateTimeOffset createdAt)
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.v8.lease",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = OutboxStatus.Pending,
            TenantId = tenantId
        };
    }

    private sealed class CapturingLeaseStore : IOutboxLeaseStore
    {
        public OutboxLeaseRequest? Request { get; private set; }

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult<IReadOnlyList<OutboxEnvelope>>([]);
        }

        public Task<bool> RenewLeaseAsync(LeaseRenewalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class NopStateWriter : IOutboxStateWriter
    {
        public Task<PersistResult> PersistAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PersistResult.Empty);
        }
    }

    private sealed class NopDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
