using LiteBus.Storage.Testing;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs outbox retention contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxRetentionStorePostgreSqlContractTests : OutboxRetentionStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxRetentionStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxRetentionStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EfCorePostgreSqlTestInfrastructure.ResetOutboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCorePostgreSqlTestInfrastructure.OutboxStoreOptions;

        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCorePostgreSqlTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString)),
            options);

        return new OutboxStoreContracts(store, store, store, store, store);
    }
}
