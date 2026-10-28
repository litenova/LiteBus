using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Release regression tests for cross-tenant conflicts and page bounds on PostgreSQL inbox stores.
/// </summary>
public sealed class PostgreSqlInboxReleaseFixesTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL test fixture shared across integration tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxReleaseFixesTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlInboxReleaseFixesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies a cross-tenant message identifier reuse conflicts even outside strict mode.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AddAsync_WhenMessageIdExistsForAnotherTenant_ShouldThrow()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePending(messageId, "tenant-a")).ConfigureAwait(false);

        var act = () => store.AddAsync(CreatePending(messageId, "tenant-b"));

        await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies batch accept with a cross-tenant message identifier reuse conflicts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AddBatchAsync_WhenMessageIdExistsForAnotherTenant_ShouldThrow()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePending(messageId, "tenant-a")).ConfigureAwait(false);

        var act = () => store.AddBatchAsync(
        [
            CreatePending(Guid.NewGuid(), "tenant-b"),
            CreatePending(messageId, "tenant-b")
        ]);

        await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a same-tenant message identifier replay still returns the existing row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AddAsync_WhenMessageIdReplaysOnSameTenant_ShouldReturnExisting()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePending(messageId, "tenant-a")).ConfigureAwait(false);
        var replay = await store.AddAsync(CreatePending(messageId, "tenant-a")).ConfigureAwait(false);

        replay.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        replay.Envelope.Id.Should().Be(messageId);
    }

    /// <summary>
    ///     Verifies message queries reject a page size above the maximum.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task QueryAsync_WhenPageSizeExceedsMaximum_ShouldThrow()
    {
        var store = await CreateStoreAsync().ConfigureAwait(false);

        var act = () => store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = InboxMessagePageRequest.MaxPageSize + 1 });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a PostgreSQL inbox store with an isolated schema for one test.
    /// </summary>
    /// <returns>The configured inbox store.</returns>
    private async Task<PostgreSqlInboxStore> CreateStoreAsync()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        return new PostgreSqlInboxStore(_fixture.DataSource, options);
    }

    /// <summary>
    ///     Creates a pending envelope with the supplied identity and tenant.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A pending inbox envelope.</returns>
    private static InboxEnvelope CreatePending(Guid messageId, string tenantId)
    {
        return new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Pending,
            AttemptCount = 0,
            TenantId = tenantId
        };
    }
}
