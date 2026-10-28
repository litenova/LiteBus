using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies a supplied message id reused across tenants conflicts instead of returning the foreign envelope.
/// </summary>
public sealed class EfCoreOutboxCrossTenantIdConflictTests : IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     Gets the base creation time used by cross-tenant envelopes.
    /// </summary>
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxCrossTenantIdConflictTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreOutboxCrossTenantIdConflictTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies the second tenant insert throws even in the default return-existing mode.
    /// </summary>
    /// <returns>A task that completes when the conflict assertion succeeds.</returns>
    [Fact]
    public async Task AddAsync_WithSameIdAcrossTenants_ShouldThrowConflict()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);

        var act = () => store.AddAsync(CreateEnvelope(messageId, "tenant-b"));

        var result = await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
        result.WithMessage("An outbox message with message id '*' already exists for a different tenant.");
    }

    /// <summary>
    ///     Verifies an enqueue batch containing a cross-tenant message identifier reuse fails instead of returning mixed results.
    /// </summary>
    /// <returns>A task that completes when the conflict assertion succeeds.</returns>
    [Fact]
    public async Task AddBatchAsync_WithSameIdAcrossTenants_ShouldThrowConflict()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);

        var act = () => store.AddBatchAsync([CreateEnvelope(Guid.NewGuid(), "tenant-b"), CreateEnvelope(messageId, "tenant-b")]);

        await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a same-tenant message identifier replay still returns the existing row.
    /// </summary>
    /// <returns>A task that completes when the dedupe assertion succeeds.</returns>
    [Fact]
    public async Task AddAsync_WhenMessageIdReplaysOnSameTenant_ShouldReturnExisting()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);
        var replay = await store.AddAsync(CreateEnvelope(messageId, "tenant-a")).ConfigureAwait(false);

        replay.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        replay.Envelope.Id.Should().Be(messageId);
    }

    /// <summary>
    ///     Verifies null, empty, and whitespace tenants compare equal as unscoped instead of conflicting.
    /// </summary>
    /// <returns>A task that completes when the dedupe assertion succeeds.</returns>
    [Fact]
    public async Task AddAsync_WhenTenantsAreUnscopedVariants_ShouldReturnExisting()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreateEnvelope(messageId, null)).ConfigureAwait(false);
        var replay = await store.AddAsync(CreateEnvelope(messageId, "   ")).ConfigureAwait(false);

        replay.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        replay.Envelope.Id.Should().Be(messageId);
    }

    /// <summary>
    ///     Creates an Entity Framework Core outbox store over a reset SQLite table for one test.
    /// </summary>
    /// <returns>The configured outbox store.</returns>
    private async Task<EfCoreOutboxStore> CreateStoreAsync()
    {
        await EfCoreProviderTestInfrastructure.ResetOutboxTableAsync(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString).ConfigureAwait(false);

        return new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateOutboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString)),
            EfCoreProviderTestInfrastructure.OutboxStoreOptions(EfCoreStorageProvider.Sqlite));
    }

    /// <summary>
    ///     Creates a pending envelope with the supplied identity and tenant.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A pending outbox envelope.</returns>
    private static OutboxEnvelope CreateEnvelope(Guid messageId, string? tenantId)
    {
        return new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.sample",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending,
            TenantId = tenantId
        };
    }
}
