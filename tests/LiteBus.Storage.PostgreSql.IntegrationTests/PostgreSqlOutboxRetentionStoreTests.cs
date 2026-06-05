using LiteBus.Outbox.Storage.PostgreSql;
using ContractTests = LiteBus.Storage.Testing.OutboxStoreContractTests;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Runs outbox retention contract tests against <see cref="PostgreSqlOutboxStore" />.
/// </summary>
public sealed class PostgreSqlOutboxRetentionStoreTests : ContractTests, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxRetentionStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override ContractTests.OutboxStoreContracts CreateStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).GetAwaiter().GetResult();
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        return new ContractTests.OutboxStoreContracts(store, store, store, store, store);
    }
}
