using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies a supplied message id reused across tenants conflicts instead of returning the foreign envelope.
/// </summary>
public sealed class EfCoreInboxCrossTenantIdConflictTests : IClassFixture<SqliteDatabaseFixture>
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
    ///     Initializes a new instance of the <see cref="EfCoreInboxCrossTenantIdConflictTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreInboxCrossTenantIdConflictTests(SqliteDatabaseFixture fixture)
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
        result.WithMessage("An inbox message with message id '*' already exists for another tenant.");
    }

    /// <summary>
    ///     Verifies an accept batch containing a cross-tenant message identifier reuse fails instead of returning mixed results.
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

        replay.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
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

        replay.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        replay.Envelope.Id.Should().Be(messageId);
    }

    /// <summary>
    ///     Verifies message queries reject a page size above the maximum.
    /// </summary>
    /// <returns>A task that completes when the bound assertion succeeds.</returns>
    [Fact]
    public async Task QueryAsync_WhenPageSizeExceedsMaximum_ShouldThrow()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);

        var act = () => store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = InboxMessagePageRequest.MaxPageSize + 1 });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies message queries still accept the maximum page size.
    /// </summary>
    /// <returns>A task that completes when the bound assertion succeeds.</returns>
    [Fact]
    public async Task QueryAsync_WhenPageSizeIsMaximum_ShouldNotThrow()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);

        var page = await store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = InboxMessagePageRequest.MaxPageSize }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Creates an Entity Framework Core inbox store over a reset SQLite table for one test.
    /// </summary>
    /// <returns>The configured inbox store.</returns>
    private async Task<EfCoreInboxStore> CreateStoreAsync()
    {
        await EfCoreProviderTestInfrastructure.ResetInboxTableAsync(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString).ConfigureAwait(false);

        return new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString)),
            EfCoreProviderTestInfrastructure.InboxStoreOptions(EfCoreStorageProvider.Sqlite));
    }

    /// <summary>
    ///     Creates a pending envelope with the supplied identity and tenant.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A pending inbox envelope.</returns>
    private static InboxEnvelope CreateEnvelope(Guid messageId, string? tenantId)
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
