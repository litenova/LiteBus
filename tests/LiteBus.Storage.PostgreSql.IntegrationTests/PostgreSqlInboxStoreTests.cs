using LiteBus.Inbox.Storage.PostgreSql;
using ContractTests = LiteBus.Storage.Testing.InboxStoreContractTests;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Runs shared inbox store contract tests against <see cref="PostgreSqlInboxStore" />.
/// </summary>
public sealed class PostgreSqlInboxStoreTests : ContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL test fixture shared across integration tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlInboxStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override ContractTests.InboxStoreRoles CreateStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).GetAwaiter().GetResult();
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        return new ContractTests.InboxStoreRoles(store, store, store);
    }
}
