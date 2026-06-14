using LiteBus.Storage.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs inbox retention contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
public sealed class EfCoreInboxRetentionStorePostgreSqlContractTests : InboxRetentionStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxRetentionStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxRetentionStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        EfCorePostgreSqlTestInfrastructure.ResetInboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCorePostgreSqlTestInfrastructure.InboxStoreOptions;

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCorePostgreSqlTestInfrastructure.CreateInboxContext(_fixture.ConnectionString)),
            options);

        return new InboxStoreRoles(store, store, store, store, store);
    }
}
