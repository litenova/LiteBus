using LiteBus.Storage.Testing;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs outbox retention contract tests against Entity Framework Core with SQL Server.
/// </summary>
public sealed class EfCoreOutboxRetentionStoreSqlServerContractTests : OutboxRetentionStoreContractTests, IClassFixture<SqlServerFixture>
{
    /// <summary>
    ///     The SQL Server fixture that supplies the connection string.
    /// </summary>
    private readonly SqlServerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxRetentionStoreSqlServerContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQL Server fixture.</param>
    public EfCoreOutboxRetentionStoreSqlServerContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EfCoreSqlServerTestInfrastructure.ResetOutboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreSqlServerTestInfrastructure.OutboxStoreOptions;

        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreSqlServerTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString)),
            options);

        return new OutboxStoreContracts(store, store, store, store, store);
    }
}
