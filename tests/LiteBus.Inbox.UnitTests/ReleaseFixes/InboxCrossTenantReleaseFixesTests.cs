using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies a message identifier reused across tenants conflicts instead of returning the foreign row.
/// </summary>
public sealed class InboxCrossTenantReleaseFixesTests
{
    /// <summary>
    ///     Gets the base creation time used by cross-tenant envelopes.
    /// </summary>
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies a cross-tenant message identifier reuse conflicts even outside strict mode.
    /// </summary>
    /// <returns>A task that completes when the conflict assertion succeeds.</returns>
    [Fact]
    public async Task AddAsync_WhenMessageIdExistsForAnotherTenant_ShouldThrow()
    {
        var store = new InMemoryInboxStore();
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);

        var act = () => store.AddAsync(CreateEnvelope(messageId, "tenant-b"));

        await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies batch accept with a cross-tenant message identifier reuse conflicts.
    /// </summary>
    /// <returns>A task that completes when the conflict assertion succeeds.</returns>
    [Fact]
    public async Task AddBatchAsync_WhenMessageIdExistsForAnotherTenant_ShouldThrow()
    {
        var store = new InMemoryInboxStore();
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);

        var act = () => store.AddBatchAsync([CreateEnvelope(messageId, "tenant-b")]);

        await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a same-tenant message identifier replay still returns the existing row.
    /// </summary>
    /// <returns>A task that completes when the dedupe assertion succeeds.</returns>
    [Fact]
    public async Task AddAsync_WhenMessageIdReplaysOnSameTenant_ShouldReturnExisting()
    {
        var store = new InMemoryInboxStore();
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);
        var replay = await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);

        replay.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        replay.Envelope.Id.Should().Be(messageId);
    }

    /// <summary>
    ///     Creates a pending envelope with the supplied identity and tenant.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A pending inbox envelope.</returns>
    private static InboxEnvelope CreateEnvelope(Guid messageId, string tenantId)
    {
        return new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            TenantId = tenantId
        };
    }
}
