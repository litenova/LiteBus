using LiteBus.Inbox.Storage.PostgreSql;
using ContractTests = LiteBus.Storage.Testing.InboxStoreContractTests;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

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

    /// <summary>
    ///     Verifies that direct PostgreSQL leasing ignores a skewed caller clock.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task LeasePendingAsync_WhenCallerClockIsSkewed_ShouldUseDatabaseClock()
    {
        return AssertDatabaseClockIgnoresCallerSkewAsync();
    }

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).GetAwaiter().GetResult();
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        return new InboxStoreRoles(store, store, store, store, store, store, store, store);
    }
}
