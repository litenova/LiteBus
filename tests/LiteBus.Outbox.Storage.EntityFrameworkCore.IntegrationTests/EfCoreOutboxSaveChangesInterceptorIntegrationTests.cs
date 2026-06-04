using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Outbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Verifies <see cref="LiteBusOutboxSaveChangesInterceptor" /> commits and rolls back with the caller transaction.
/// </summary>
public sealed class EfCoreOutboxSaveChangesInterceptorIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxSaveChangesInterceptorIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxSaveChangesInterceptorIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms rolled-back <c>SaveChanges</c> does not persist queued outbox rows.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ShouldNotPersistOutboxMessage_WhenTransactionRollsBack()
    {
        var storeOptions = await CreateOutboxTableAsync().ConfigureAwait(false);
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        await using var context = CreateContext(storeOptions, interceptor);
        var envelope = CreateEnvelope();

        await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        interceptor.Enqueue(envelope);

        var savedCount = await context.SaveChangesAsync().ConfigureAwait(false);
        savedCount.Should().Be(1);

        await transaction.RollbackAsync().ConfigureAwait(false);

        await using var verificationContext = CreateContext(storeOptions);
        var storedCount = await verificationContext.OutboxMessages
            .CountAsync(message => message.Id == envelope.Id)
            .ConfigureAwait(false);

        storedCount.Should().Be(0);
    }

    /// <summary>
    ///     Confirms committed <c>SaveChanges</c> persists queued outbox rows.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ShouldPersistOutboxMessage_WhenTransactionCommits()
    {
        var storeOptions = await CreateOutboxTableAsync().ConfigureAwait(false);
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        await using var context = CreateContext(storeOptions, interceptor);
        var envelope = CreateEnvelope();

        await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        interceptor.Enqueue(envelope);

        var savedCount = await context.SaveChangesAsync().ConfigureAwait(false);
        savedCount.Should().Be(1);

        await transaction.CommitAsync().ConfigureAwait(false);

        await using var verificationContext = CreateContext(storeOptions);
        var storedMessage = await verificationContext.OutboxMessages
            .SingleOrDefaultAsync(message => message.Id == envelope.Id)
            .ConfigureAwait(false);

        storedMessage.Should().NotBeNull();
        storedMessage!.ContractName.Should().Be(envelope.ContractName);
        storedMessage.Payload.Should().Contain("orderId");
        storedMessage.Status.Should().Be(OutboxStatus.Pending);
    }

    /// <summary>
    ///     Creates an isolated outbox table for one test run.
    /// </summary>
    /// <returns>The store options for the created table.</returns>
    private async Task<EfCoreOutboxStoreOptions> CreateOutboxTableAsync()
    {
        var options = new EfCoreOutboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"outbox_ef_atomic_{Guid.NewGuid():N}"
        };

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(_fixture.ConnectionString);
        await PostgreSqlOutboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlOutboxStoreOptions
            {
                SchemaName = options.SchemaName,
                TableName = options.TableName,
                ValidateSchemaCreationOnStartup = false
            }).ConfigureAwait(false);

        return options;
    }

    /// <summary>
    ///     Creates a database context for the test.
    /// </summary>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="interceptor">The optional save-changes interceptor.</param>
    /// <returns>The configured context.</returns>
    private IntegrationOutboxDbContext CreateContext(
        EfCoreOutboxStoreOptions storeOptions,
        LiteBusOutboxSaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            builder.AddLiteBusOutboxInterceptor(interceptor);
        }

        return new IntegrationOutboxDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Creates a sample outbox envelope.
    /// </summary>
    /// <returns>The envelope used by tests.</returns>
    private static OutboxEnvelope CreateEnvelope()
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"123"}""",
            Topic = "orders",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }
}
