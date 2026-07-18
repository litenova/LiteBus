using AwesomeAssertions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Enterprise.UnitTests;

/// <summary>
///     Verifies per-tenant lease isolation for inbox and outbox stores.
/// </summary>
public sealed class TenantLeaseFilterTests
{
    /// <summary>
    ///     Verifies inbox leasing honors the tenant filter on the lease request.
    /// </summary>
    [Fact]
    public async Task InboxLeaseRequest_FiltersByTenantId()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;

        await store.AddAsync(CreateInboxEnvelope("tenant-a")).ConfigureAwait(false);
        await store.AddAsync(CreateInboxEnvelope("tenant-b")).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1),
            TenantId = "tenant-a"
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].TenantId.Should().Be("tenant-a");
    }

    /// <summary>
    ///     Verifies outbox leasing honors the tenant filter on the lease request.
    /// </summary>
    [Fact]
    public async Task OutboxLeaseRequest_FiltersByTenantId()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;

        await store.AddAsync(CreateOutboxEnvelope("tenant-a")).ConfigureAwait(false);
        await store.AddAsync(CreateOutboxEnvelope("tenant-b")).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1),
            TenantId = "tenant-b"
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].TenantId.Should().Be("tenant-b");
    }

    /// <summary>
    ///     Creates one pending inbox envelope for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The inbox envelope.</returns>
    private static InboxEnvelope CreateInboxEnvelope(string tenantId)
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "test",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Pending,
            AttemptCount = 0,
            TenantId = tenantId
        };
    }

    /// <summary>
    ///     Creates one pending outbox envelope for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The outbox envelope.</returns>
    private static OutboxEnvelope CreateOutboxEnvelope(string tenantId)
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "test",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            TenantId = tenantId
        };
    }
}
