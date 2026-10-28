using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies a supplied message id reused across tenants conflicts instead of returning the foreign envelope.
/// </summary>
public sealed class PostgreSqlOutboxCrossTenantIdConflictTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxCrossTenantIdConflictTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies the second tenant insert throws even in the default return-existing mode.
    /// </summary>
    [SkippableFact]
    public async Task AddAsync_with_same_id_across_tenants_should_throw_conflict()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.sample",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            TenantId = "tenant-a"
        }).ConfigureAwait(false);

        var act = () => store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.sample",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            TenantId = "tenant-b"
        });

        await act.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
    }
}
