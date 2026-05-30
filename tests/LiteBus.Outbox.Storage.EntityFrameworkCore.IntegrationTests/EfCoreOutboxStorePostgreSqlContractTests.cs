using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Runs shared outbox store contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
public sealed class EfCoreOutboxStorePostgreSqlContractTests : OutboxStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EfCorePostgreSqlTestInfrastructure.ResetOutboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCorePostgreSqlTestInfrastructure.OutboxOptions;
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCorePostgreSqlTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString)),
            options);
        return new OutboxStoreContracts(store, store, store);
    }
}
