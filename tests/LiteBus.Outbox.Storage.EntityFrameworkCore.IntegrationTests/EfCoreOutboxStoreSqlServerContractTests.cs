using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Runs shared outbox store contract tests against Entity Framework Core with SQL Server.
/// </summary>
public sealed class EfCoreOutboxStoreSqlServerContractTests : OutboxStoreContractTests, IClassFixture<SqlServerFixture>
{
    /// <summary>
    ///     The SQL Server fixture that supplies the connection string.
    /// </summary>
    private readonly SqlServerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStoreSqlServerContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQL Server fixture.</param>
    public EfCoreOutboxStoreSqlServerContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EfCoreSqlServerTestInfrastructure.ResetOutboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreSqlServerTestInfrastructure.OutboxOptions;
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreSqlServerTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString)),
            options);
        return new OutboxStoreContracts(store, store, store);
    }
}
