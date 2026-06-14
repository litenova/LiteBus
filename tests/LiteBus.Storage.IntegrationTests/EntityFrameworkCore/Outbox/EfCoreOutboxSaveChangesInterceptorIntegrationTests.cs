using System.Text.Json;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies <see cref="LiteBusOutboxSaveChangesInterceptor" /> commits and rolls back with the caller transaction.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
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

         var context = CreateContext(storeOptions, interceptor);
         await using (context.ConfigureAwait(false))
         {
        var envelope = CreateEnvelope();

         var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
         await using (transaction.ConfigureAwait(false))
         {
        interceptor.Enqueue(context, envelope);

        var savedCount = await context.SaveChangesAsync().ConfigureAwait(false);
        savedCount.Should().Be(1);

        await transaction.RollbackAsync().ConfigureAwait(false);

         var verificationContext = CreateContext(storeOptions);
         await using (verificationContext.ConfigureAwait(false))
         {

        var storedCount = await verificationContext.OutboxMessages
            .CountAsync(message => message.Id == envelope.Id).ConfigureAwait(false);

        storedCount.Should().Be(0);
        }
        }
        }
    }

    /// <summary>
    ///     Confirms committed <c>SaveChanges</c> persists queued outbox rows.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ShouldPersistOutboxMessage_WhenTransactionCommits()
    {
        var storeOptions = await CreateOutboxTableAsync().ConfigureAwait(false);
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

         var context = CreateContext(storeOptions, interceptor);
         await using (context.ConfigureAwait(false))
         {
        var envelope = CreateEnvelope();

         var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
         await using (transaction.ConfigureAwait(false))
         {
        interceptor.Enqueue(context, envelope);

        var savedCount = await context.SaveChangesAsync().ConfigureAwait(false);
        savedCount.Should().Be(1);

        await transaction.CommitAsync().ConfigureAwait(false);

         var verificationContext = CreateContext(storeOptions);
         await using (verificationContext.ConfigureAwait(false))
         {

        var storedMessage = await verificationContext.OutboxMessages
            .SingleOrDefaultAsync(message => message.Id == envelope.Id).ConfigureAwait(false);

        storedMessage.Should().NotBeNull();
        storedMessage!.ContractName.Should().Be(envelope.ContractName);
        storedMessage.Payload.Should().Contain("orderId");
        storedMessage.Status.Should().Be(OutboxStatus.Pending);
        }
        }
        }
    }

    /// <summary>
    ///     Confirms every <see cref="OutboxEnvelope" /> field round-trips through the interceptor into persistence.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ShouldPersistEveryEnvelopeField_IncludingIdempotencyKeyAndTraceContext()
    {
        var storeOptions = await CreateOutboxTableAsync().ConfigureAwait(false);
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 2,
            Payload = """{"orderId":"456"}""",
            Topic = "orders",
            CreatedAt = new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.Zero),
            VisibleAfter = new DateTimeOffset(2026, 6, 4, 8, 30, 0, TimeSpan.Zero),
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = "corr-99",
            CausationId = "cause-99",
            TenantId = "tenant-99",
            IdempotencyKey = "idem-99",
            TraceContext = """{"traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"}"""
        };

         var context = CreateContext(storeOptions, interceptor);
         await using (context.ConfigureAwait(false))
         {
        interceptor.Enqueue(context, envelope);
        await context.SaveChangesAsync().ConfigureAwait(false);

         var verificationContext = CreateContext(storeOptions);
         await using (verificationContext.ConfigureAwait(false))
         {

        var storedMessage = await verificationContext.OutboxMessages
            .SingleAsync(message => message.Id == envelope.Id).ConfigureAwait(false);

        storedMessage.ContractName.Should().Be(envelope.ContractName);
        storedMessage.ContractVersion.Should().Be(envelope.ContractVersion);
        NormalizeJson(storedMessage.Payload).Should().Be(NormalizeJson(envelope.Payload));
        storedMessage.Topic.Should().Be(envelope.Topic);
        storedMessage.CreatedAt.Should().Be(envelope.CreatedAt);
        storedMessage.VisibleAfter.Should().Be(envelope.VisibleAfter);
        storedMessage.Status.Should().Be(envelope.Status);
        storedMessage.AttemptCount.Should().Be(envelope.AttemptCount);
        storedMessage.CorrelationId.Should().Be(envelope.CorrelationId);
        storedMessage.CausationId.Should().Be(envelope.CausationId);
        storedMessage.TenantId.Should().Be(envelope.TenantId);
        storedMessage.IdempotencyKey.Should().Be(envelope.IdempotencyKey);
        NormalizeJson(storedMessage.TraceContext!).Should().Be(NormalizeJson(envelope.TraceContext!));
        }
        }
    }

    /// <summary>
    ///     Creates an isolated outbox table for one test run.
    /// </summary>
    /// <returns>The store options for the created table.</returns>
    private async Task<EntityFrameworkCoreOutboxStoreOptions> CreateOutboxTableAsync()
    {
        var options = new EntityFrameworkCoreOutboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"outbox_ef_atomic_{Guid.NewGuid():N}"
        };

         var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
         await using (dataSource.ConfigureAwait(false))
         {

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
    }

    /// <summary>
    ///     Creates a database context for the test.
    /// </summary>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="interceptor">The optional save-changes interceptor.</param>
    /// <returns>The configured context.</returns>
    private InterceptorOutboxDbContext CreateContext(
        EntityFrameworkCoreOutboxStoreOptions storeOptions,
        LiteBusOutboxSaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<InterceptorOutboxDbContext>()
            .UseNpgsql(EfCorePostgreSqlTestInfrastructure.CreateScopedConnectionString(_fixture.ConnectionString, storeOptions));

        if (interceptor is not null)
        {
            builder.AddLiteBusOutboxInterceptor(interceptor);
        }

        return new InterceptorOutboxDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Creates a sample outbox envelope.
    /// </summary>
    /// <returns>The envelope used by tests.</returns>
    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

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
