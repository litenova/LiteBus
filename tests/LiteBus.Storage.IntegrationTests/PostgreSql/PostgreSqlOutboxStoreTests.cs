using LiteBus.Outbox.Storage.PostgreSql;
using ContractTests = LiteBus.Storage.Testing.OutboxStoreContractTests;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Runs shared outbox store contract tests against <see cref="PostgreSqlOutboxStore" />.
/// </summary>
public sealed class PostgreSqlOutboxStoreTests : ContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL test fixture shared across integration tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlOutboxStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).GetAwaiter().GetResult();
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        return new OutboxStoreContracts(store, store, store, store, store, store, store, store);
    }
}