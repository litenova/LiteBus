using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies PostgreSQL lease SQL preserves the idempotency key on leased outbox envelopes.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxLeasePostgreSqlIdempotencyKeyTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxLeasePostgreSqlIdempotencyKeyTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxLeasePostgreSqlIdempotencyKeyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms leased envelopes carry the stored idempotency key.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public Task LeasePendingAsync_ShouldPreserveIdempotencyKey()
    {
        return OutboxLeaseIdempotencyKeyHarness.LeaseAndAssertKeyAsync(
            EfCoreStorageProvider.PostgreSql,
            _fixture.ConnectionString);
    }
}

/// <summary>
///     Verifies SQL Server lease SQL preserves the idempotency key on leased outbox envelopes.
/// </summary>
[Collection(SqlServerOutboxCollection.Name)]
public sealed class EfCoreOutboxLeaseSqlServerIdempotencyKeyTests : IClassFixture<SqlServerFixture>
{
    /// <summary>
    ///     The SQL Server fixture that supplies the connection string.
    /// </summary>
    private readonly SqlServerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxLeaseSqlServerIdempotencyKeyTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQL Server fixture.</param>
    public EfCoreOutboxLeaseSqlServerIdempotencyKeyTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, SqlServerFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms leased envelopes carry the stored idempotency key.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public Task LeasePendingAsync_ShouldPreserveIdempotencyKey()
    {
        return OutboxLeaseIdempotencyKeyHarness.LeaseAndAssertKeyAsync(
            EfCoreStorageProvider.SqlServer,
            _fixture.ConnectionString);
    }
}

/// <summary>
///     Verifies MySQL lease SQL preserves the idempotency key on leased outbox envelopes.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class EfCoreOutboxLeaseMySqlIdempotencyKeyTests
{
    /// <summary>
    ///     The MySQL fixture that owns the shared server.
    /// </summary>
    private readonly MySqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxLeaseMySqlIdempotencyKeyTests" /> class.
    /// </summary>
    /// <param name="fixture">The MySQL fixture.</param>
    public EfCoreOutboxLeaseMySqlIdempotencyKeyTests(MySqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, MySqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms leased envelopes carry the stored idempotency key.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public Task LeasePendingAsync_ShouldPreserveIdempotencyKey()
    {
        return OutboxLeaseIdempotencyKeyHarness.LeaseAndAssertKeyAsync(
            EfCoreStorageProvider.MySql,
            _fixture.ConnectionString);
    }
}

/// <summary>
///     Leases one keyed outbox row and asserts the leased envelope carries the key.
/// </summary>
internal static class OutboxLeaseIdempotencyKeyHarness
{
    /// <summary>
    ///     Resets the provider table, enqueues one keyed envelope, leases it, and asserts the key round-trips.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <param name="connectionString">The provider connection string.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    internal static async Task LeaseAndAssertKeyAsync(EfCoreStorageProvider provider, string connectionString)
    {
        await EfCoreProviderTestInfrastructure.ResetOutboxTableAsync(provider, connectionString).ConfigureAwait(false);

        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateOutboxContext(provider, connectionString)),
            EfCoreProviderTestInfrastructure.OutboxStoreOptions(provider));

        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        await store.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"1"}""",
            Topic = "tests",
            CreatedAt = now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = "order-1"
        }).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].IdempotencyKey.Should().Be("order-1");
    }
}
