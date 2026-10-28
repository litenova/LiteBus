using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies blank tenant filters match unscoped rows on the PostgreSQL outbox store.
/// </summary>
public sealed class PostgreSqlOutboxTenantFilterNormalizationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxTenantFilterNormalizationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies a whitespace lease tenant leases unscoped rows like the inbox store does.
    /// </summary>
    [SkippableFact]
    public async Task LeasePendingAsync_with_whitespace_tenant_should_lease_unscoped_rows()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);

        await store.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.sample",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        }).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = "tenant-test",
            LeaseDuration = TimeSpan.FromMinutes(1),
            Now = DateTimeOffset.UtcNow,
            TenantId = "   "
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
    }

    /// <summary>
    ///     Verifies a whitespace query tenant matches unscoped rows.
    /// </summary>
    [SkippableFact]
    public async Task QueryAsync_with_whitespace_tenant_should_match_unscoped_rows()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);

        await store.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.sample",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        }).ConfigureAwait(false);

        var page = await store.QueryAsync(
            new OutboxMessageFilter { TenantId = "  " },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().ContainSingle();
    }
}
