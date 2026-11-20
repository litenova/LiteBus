using LiteBus.Auditing;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;
using LiteBus.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Collections.Concurrent;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.CommandBatches;

/// <summary>
///     Verifies command batches commit or roll back domain rows, transactional outbox rows, idempotency keys,
///     and audit records together through Entity Framework Core and PostgreSQL.
/// </summary>
public sealed class EfCoreCommandBatchTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The domain orders table shared by every test in this class.
    /// </summary>
    private const string DomainTableName = "cmd_batch_orders";

    /// <summary>
    ///     The outbox table shared by every test in this class.
    /// </summary>
    private const string OutboxTableName = "outbox_ef_cmd_batch";

    /// <summary>
    ///     The audit table shared by every test in this class.
    /// </summary>
    private const string AuditTableName = "cmd_batch_audit";

    /// <summary>
    ///     The audit record property that carries the batch identifier.
    /// </summary>
    private const string BatchIdPropertyName = "litebus.batch.id";

    /// <summary>
    ///     The failure code given to a record whose command passed but whose batch rolled back.
    /// </summary>
    private const string RolledBackFailureCode = "litebus.batch.rolled_back";

    /// <summary>
    ///     The audit action declared by the order command.
    /// </summary>
    private const string ApplyOrderAction = "batches.apply-order";

    /// <summary>
    ///     The outbox contract name of the staged order event.
    /// </summary>
    private const string OrderSubmittedContract = "batches.events.order-submitted";

    /// <summary>
    ///     The idempotency scope shared by the order commands.
    /// </summary>
    private const string IdempotencyScope = "command-batches";

    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreCommandBatchTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreCommandBatchTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms three commands commit their domain rows and their transactional outbox rows together.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WithThreeCommands_ShouldCommitDomainAndOutboxRowsTogether()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: false),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var result = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.Committed);
        result.FailedIndex.Should().BeNull();
        result.Entries.Should().HaveCount(3);

        for (var index = 0; index < result.Entries.Count; index++)
        {
            var entry = result.Entries[index];
            entry.Index.Should().Be(index);
            entry.Executed.Should().BeTrue();
            entry.Result.Should().NotBeNull();
            entry.Result!.Value.Outcome.Should().Be(MediationOutcome.Succeeded);
        }

        (await CountDomainRowsAsync(outboxStoreOptions, [firstId, secondId, thirdId]).ConfigureAwait(false)).Should().Be(3);
        (await CountOutboxRowsForAsync(outboxStoreOptions, firstId).ConfigureAwait(false)).Should().Be(1);
        (await CountOutboxRowsForAsync(outboxStoreOptions, secondId).ConfigureAwait(false)).Should().Be(1);
        (await CountOutboxRowsForAsync(outboxStoreOptions, thirdId).ConfigureAwait(false)).Should().Be(1);

        counter.CountFor(firstId).Should().Be(1);
        counter.CountFor(secondId).Should().Be(1);
        counter.CountFor(thirdId).Should().Be(1);
    }

    /// <summary>
    ///     Confirms a denial of the second command leaves no domain rows and no outbox rows from any command.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WhenSecondCommandIsDenied_ShouldRollBackDomainAndOutboxRows()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: true),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var result = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.RolledBack);
        result.FailedIndex.Should().Be(1);
        result.Entries.Should().HaveCount(3);
        result.Entries[0].Executed.Should().BeTrue();
        result.Entries[0].Result.Should().NotBeNull();
        result.Entries[0].Result!.Value.Outcome.Should().Be(MediationOutcome.Succeeded);
        result.Entries[1].Executed.Should().BeTrue();
        result.Entries[1].Result.Should().NotBeNull();
        result.Entries[1].Result!.Value.Outcome.Should().Be(MediationOutcome.Denied);
        result.Entries[2].Executed.Should().BeFalse();
        result.Entries[2].Result.Should().BeNull();

        (await CountDomainRowsAsync(outboxStoreOptions, [firstId, secondId, thirdId]).ConfigureAwait(false)).Should().Be(0);
        (await CountOutboxRowsForAsync(outboxStoreOptions, firstId).ConfigureAwait(false)).Should().Be(0);
        (await CountOutboxRowsForAsync(outboxStoreOptions, secondId).ConfigureAwait(false)).Should().Be(0);
        (await CountOutboxRowsForAsync(outboxStoreOptions, thirdId).ConfigureAwait(false)).Should().Be(0);

        counter.CountFor(firstId).Should().Be(1);
        counter.CountFor(secondId).Should().Be(0);
        counter.CountFor(thirdId).Should().Be(0);

        var sendFirstId = Guid.NewGuid();
        var sendSecondId = Guid.NewGuid();
        IReadOnlyList<ICommand> refused =
        [
            new ApplyBatchOrderCommand(sendFirstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(sendSecondId, 20m, Blocked: true)
        ];

        var send = async () => await batches.SendManyAsync(refused).ConfigureAwait(false);

        await send.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        (await CountDomainRowsAsync(outboxStoreOptions, [sendFirstId, sendSecondId]).ConfigureAwait(false)).Should().Be(0);
    }

    /// <summary>
    ///     Confirms a command answered by a shortcut does not stop the batch.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WithAnsweredCommand_ShouldCommitWithoutStopping()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var answeredId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new AnswerBatchOrderCommand(answeredId),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var result = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.Committed);
        result.FailedIndex.Should().BeNull();
        result.Entries.Should().HaveCount(3);
        result.Entries[0].Executed.Should().BeTrue();
        result.Entries[0].Result.Should().NotBeNull();
        result.Entries[0].Result!.Value.Outcome.Should().Be(MediationOutcome.Succeeded);
        result.Entries[1].Executed.Should().BeTrue();
        result.Entries[1].Result.Should().NotBeNull();
        result.Entries[1].Result!.Value.Outcome.Should().Be(MediationOutcome.Answered);
        result.Entries[2].Executed.Should().BeTrue();
        result.Entries[2].Result.Should().NotBeNull();
        result.Entries[2].Result!.Value.Outcome.Should().Be(MediationOutcome.Succeeded);

        (await CountDomainRowsAsync(outboxStoreOptions, [firstId, thirdId]).ConfigureAwait(false)).Should().Be(2);
        (await CountDomainRowsAsync(outboxStoreOptions, [answeredId]).ConfigureAwait(false)).Should().Be(0);
        (await CountOutboxRowsForAsync(outboxStoreOptions, firstId).ConfigureAwait(false)).Should().Be(1);
        (await CountOutboxRowsForAsync(outboxStoreOptions, thirdId).ConfigureAwait(false)).Should().Be(1);

        counter.CountFor(firstId).Should().Be(1);
        counter.CountFor(answeredId).Should().Be(0);
        counter.CountFor(thirdId).Should().Be(1);
    }

    /// <summary>
    ///     Confirms the PostgreSQL idempotency keys of a committed batch are completed.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WhenBatchCommits_ShouldCompleteIdempotencyKeys()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: false)
        ];

        var result = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.Committed);

        var keys = await ReadIdempotencyKeysAsync(dataSource, idempotencyOptions).ConfigureAwait(false);

        keys.Should().HaveCount(2);
        keys.Should().OnlyContain(key => key.IsCompleted);
        keys.Should().ContainSingle(key => key.Key.Contains(firstId.ToString("N"), StringComparison.Ordinal));
        keys.Should().ContainSingle(key => key.Key.Contains(secondId.ToString("N"), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms the idempotency keys of a rolled-back batch are released, so a rerun executes every handler again.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WhenBatchRollsBack_ShouldReleaseIdempotencyKeysAndRerunHandlers()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: true),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var rolledBack = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        rolledBack.Outcome.Should().Be(CommandBatchOutcome.RolledBack);

        var released = await ReadIdempotencyKeysAsync(dataSource, idempotencyOptions).ConfigureAwait(false);

        released.Should().BeEmpty();
        (await CountDomainRowsAsync(outboxStoreOptions, [firstId, secondId, thirdId]).ConfigureAwait(false)).Should().Be(0);

        IReadOnlyList<ICommand> rerun =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: false),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var committed = await batches.TrySendManyAsync(rerun).ConfigureAwait(false);

        committed.Outcome.Should().Be(CommandBatchOutcome.Committed);
        committed.Entries.Should().HaveCount(3);

        foreach (var entry in committed.Entries)
        {
            entry.Executed.Should().BeTrue();
            entry.Result.Should().NotBeNull();
            entry.Result!.Value.Outcome.Should().Be(MediationOutcome.Succeeded);
        }

        counter.CountFor(firstId).Should().Be(2);
        counter.CountFor(secondId).Should().Be(1);
        counter.CountFor(thirdId).Should().Be(1);

        (await CountDomainRowsAsync(outboxStoreOptions, [firstId, secondId, thirdId]).ConfigureAwait(false)).Should().Be(3);

        var completed = await ReadIdempotencyKeysAsync(dataSource, idempotencyOptions).ConfigureAwait(false);

        completed.Should().HaveCount(3);
        completed.Should().OnlyContain(key => key.IsCompleted);
    }

    /// <summary>
    ///     Confirms a committed batch writes one audit record per command, each carrying the batch identifier.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WhenBatchCommits_ShouldWriteAuditRecordsWithBatchId()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: false),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var result = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.Committed);

        string[] targets = [firstId.ToString("N"), secondId.ToString("N"), thirdId.ToString("N")];
        var rows = await ReadAuditRowsAsync(targets).ConfigureAwait(false);

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Action == ApplyOrderAction);
        rows.Should().OnlyContain(row => row.Outcome == (int) AuditOutcome.Succeeded);
        rows.Select(row => row.TargetId).Should().BeEquivalentTo(targets);
        rows.Select(row => row.BatchId).Distinct().Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    ///     Confirms a rolled-back batch rewrites passed commands to failed while the stopping command keeps its own outcome.
    /// </summary>
    [SkippableFact]
    public async Task TrySendManyAsync_WhenBatchRollsBack_ShouldRewriteAuditRecordsForRollback()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplyBatchOrderCommand(secondId, 20m, Blocked: true),
            new ApplyBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var result = await batches.TrySendManyAsync(commands).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.RolledBack);

        string[] targets = [firstId.ToString("N"), secondId.ToString("N"), thirdId.ToString("N")];

        // The handler names the audit target, and a guard-denied command never reaches its handler, so the
        // denial record has no target. Find the batch through the passed record, then read every record of it.
        var passed = (await ReadAuditRowsAsync(targets).ConfigureAwait(false)).Should().ContainSingle().Which;
        passed.TargetId.Should().Be(targets[0]);
        passed.Outcome.Should().Be((int) AuditOutcome.Failed);
        passed.FailureCode.Should().Be(RolledBackFailureCode);
        passed.BatchId.Should().NotBeNullOrWhiteSpace();

        var rows = await ReadAuditRowsByBatchAsync(passed.BatchId!).ConfigureAwait(false);
        rows.Should().HaveCount(2);

        var stopping = rows.Should().ContainSingle(row => row.Id != passed.Id).Which;
        stopping.Outcome.Should().Be((int) AuditOutcome.Denied);
    }

    /// <summary>
    ///     Confirms a batch started from a handler inside another batch is refused and rolls the outer batch back.
    /// </summary>
    [SkippableFact]
    public async Task SendManyAsync_WhenHandlerStartsNestedBatch_ShouldThrowAndRollBack()
    {
        var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var configuredDataSource = dataSource.ConfigureAwait(false);
        var outboxStoreOptions = OutboxStoreOptions();
        await EnsureTablesAsync(dataSource, outboxStoreOptions).ConfigureAwait(false);
        var idempotencyOptions = CreateIdempotencyOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(dataSource, idempotencyOptions).ConfigureAwait(false);
        var counter = new BatchHandlerCounter();

        var provider = BuildProvider(_fixture.ConnectionString, dataSource, outboxStoreOptions, idempotencyOptions, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var nestedId = Guid.NewGuid();
        IReadOnlyList<ICommand> commands =
        [
            new ApplyBatchOrderCommand(firstId, 10m, Blocked: false),
            new NestedBatchOrderCommand(nestedId)
        ];

        var nested = async () => await batches.SendManyAsync(commands).ConfigureAwait(false);

        await nested.Should().ThrowAsync<InvalidOperationException>()
            .Where(exception => exception.Message.Contains("cannot start inside another", StringComparison.Ordinal))
            .ConfigureAwait(false);

        (await CountDomainRowsAsync(outboxStoreOptions, [firstId, nestedId]).ConfigureAwait(false)).Should().Be(0);
        (await CountOutboxRowsForAsync(outboxStoreOptions, firstId).ConfigureAwait(false)).Should().Be(0);
    }

    /// <summary>
    ///     Gets the outbox store options shared by every test in this class.
    /// </summary>
    /// <returns>The store options pointing at the shared outbox table.</returns>
    private static EntityFrameworkCoreOutboxStoreOptions OutboxStoreOptions()
    {
        return new EntityFrameworkCoreOutboxStoreOptions
        {
            SchemaName = EfCoreProviderTestInfrastructure.SchemaName,
            TableName = OutboxTableName
        };
    }

    /// <summary>
    ///     Creates idempotency store options against a fresh table for one test run.
    /// </summary>
    /// <returns>The store options.</returns>
    private static PostgreSqlIdempotencyStoreOptions CreateIdempotencyOptions()
    {
        return new PostgreSqlIdempotencyStoreOptions
        {
            SchemaName = EfCoreProviderTestInfrastructure.SchemaName,
            TableName = $"cmd_batch_idem_{Guid.NewGuid():N}"
        };
    }

    /// <summary>
    ///     Creates the outbox, domain, and audit tables when they do not exist yet.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task EnsureTablesAsync(NpgsqlDataSource dataSource, EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        await PostgreSqlOutboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlOutboxStoreOptions
            {
                SchemaName = storeOptions.SchemaName,
                TableName = storeOptions.TableName,
                ValidateSchemaCreationOnStartup = false
            }).ConfigureAwait(false);

        var batchContext = CreateBatchContext(_fixture.ConnectionString, storeOptions);
        await using (batchContext.ConfigureAwait(false))
        {
            var qualifiedOrdersTable = PostgreSqlIdentifier.Qualify(storeOptions.SchemaName, DomainTableName);

#pragma warning disable EF1002 // PostgreSqlIdentifier validates and quotes the generated schema and table identifiers.
            await batchContext.Database.ExecuteSqlRawAsync(
                $"""
                 CREATE TABLE IF NOT EXISTS {qualifiedOrdersTable} (
                     order_id uuid NOT NULL PRIMARY KEY,
                     total numeric NOT NULL);
                 """).ConfigureAwait(false);
#pragma warning restore EF1002
        }

        var auditContext = CreateAuditContext(_fixture.ConnectionString);
        await using (auditContext.ConfigureAwait(false))
        {
            var qualifiedAuditTable = PostgreSqlIdentifier.Qualify(storeOptions.SchemaName, AuditTableName);

#pragma warning disable EF1002 // PostgreSqlIdentifier validates and quotes the generated schema and table identifiers.
            await auditContext.Database.ExecuteSqlRawAsync(
                $"""
                 CREATE TABLE IF NOT EXISTS {qualifiedAuditTable} (
                     id uuid NOT NULL PRIMARY KEY,
                     action text NOT NULL,
                     outcome integer NOT NULL,
                     failure_code text NULL,
                     batch_id text NULL,
                     target_id text NULL,
                     message_type text NULL,
                     recorded_at timestamptz NOT NULL);
                 """).ConfigureAwait(false);
#pragma warning restore EF1002
        }
    }

    /// <summary>
    ///     Creates a batch database context against the shared container.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The configured context.</returns>
    private static BatchDbContext CreateBatchContext(string connectionString, EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        var builder = new DbContextOptionsBuilder<BatchDbContext>()
            .UseNpgsql(connectionString);

        return new BatchDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Creates an audit database context against the shared container.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The configured context.</returns>
    private static BatchAuditDbContext CreateAuditContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<BatchAuditDbContext>()
            .UseNpgsql(connectionString);

        return new BatchAuditDbContext(builder.Options);
    }

    /// <summary>
    ///     Builds a provider with the batch commands, the Entity Framework Core outbox, the PostgreSQL
    ///     idempotency store, the Entity Framework Core audit trail, and the batch unit of work.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="dataSource">The PostgreSQL data source shared with the idempotency store.</param>
    /// <param name="outboxStoreOptions">The outbox store options.</param>
    /// <param name="idempotencyOptions">The idempotency store options.</param>
    /// <param name="counter">Counts how many times each handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        string connectionString,
        NpgsqlDataSource dataSource,
        EntityFrameworkCoreOutboxStoreOptions outboxStoreOptions,
        PostgreSqlIdempotencyStoreOptions idempotencyOptions,
        BatchHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddSingleton(outboxStoreOptions);
        services.AddSingleton(counter);
        services.AddDbContextFactory<BatchDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContextFactory<BatchAuditDbContext>(options => options.UseNpgsql(connectionString));
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseTrail<EfCoreBatchAuditTrail>());
            registry.AddCommands(commands =>
            {
                commands.Register<ApplyBatchOrderCommand>();
                commands.Register<ApplyBatchOrderCommandHandler>();
                commands.Register<ApplyBatchOrderGuard>();
                commands.Register<ApplyBatchOrderCommandDefinition>();
                commands.Register<AnswerBatchOrderCommand>();
                commands.Register<AnswerBatchOrderCommandHandler>();
                commands.Register<AnswerBatchOrderShortcut>();
                commands.Register<NestedBatchOrderCommand>();
                commands.Register<NestedBatchOrderCommandHandler>();
                commands.EnableIdempotency();
                commands.UsePostgreSqlIdempotencyStore(pg =>
                {
                    pg.UseDataSource(dataSource);
                    pg.UseOptions(idempotencyOptions);
                    pg.DisableSchemaInitialization();
                });
                commands.UseUnitOfWork<BatchOrderUnitOfWork>();
            });
            registry.AddOutbox(outbox =>
            {
                outbox.UseEntityFrameworkCoreStorage(ef =>
                {
                    ef.UseDbContext<BatchDbContext>();
                    ef.UseOptions(outboxStoreOptions);
                    ef.EnableSaveChangesInterceptor();
                });
                outbox.Contracts.Register<BatchOrderSubmittedEvent>(OrderSubmittedContract);
            });
        });
        services.AddDbContext<BatchDbContext>((serviceProvider, options) => options
            .UseNpgsql(connectionString)
            .AddLiteBusOutboxInterceptor(serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Counts the domain rows stored for the given order identifiers.
    /// </summary>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="orderIds">The order identifiers to count.</param>
    /// <returns>The number of stored domain rows.</returns>
    private async Task<int> CountDomainRowsAsync(EntityFrameworkCoreOutboxStoreOptions storeOptions, Guid[] orderIds)
    {
        var context = CreateBatchContext(_fixture.ConnectionString, storeOptions);
        await using (context.ConfigureAwait(false))
        {
            return await context.Orders.CountAsync(order => orderIds.Contains(order.OrderId)).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts the outbox rows whose payload carries the given order identifier.
    /// </summary>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="orderId">The order identifier to look for.</param>
    /// <returns>The number of stored outbox rows.</returns>
    private async Task<int> CountOutboxRowsForAsync(EntityFrameworkCoreOutboxStoreOptions storeOptions, Guid orderId)
    {
        var context = CreateBatchContext(_fixture.ConnectionString, storeOptions);
        await using (context.ConfigureAwait(false))
        {
            var marker = orderId.ToString();

            return await context.OutboxMessages.CountAsync(message => message.Payload.Contains(marker)).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reads every stored idempotency key from the table the test created.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The store options identifying the table.</param>
    /// <returns>The stored keys and their settlement state.</returns>
    private static async Task<IReadOnlyList<StoredIdempotencyKey>> ReadIdempotencyKeysAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlIdempotencyStoreOptions options)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var configuredConnection = connection.ConfigureAwait(false);

        var command = new NpgsqlCommand(
            $"SELECT idempotency_key, is_completed, payload FROM {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)}",
            connection);
        await using var configuredCommand = command.ConfigureAwait(false);

        var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        await using var configuredReader = reader.ConfigureAwait(false);

        List<StoredIdempotencyKey> keys = [];

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            keys.Add(new StoredIdempotencyKey(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return keys;
    }

    /// <summary>
    ///     Reads every audit row stored for one batch.
    /// </summary>
    /// <param name="batchId">The batch identifier recorded on the rows.</param>
    /// <returns>The stored audit rows of the batch.</returns>
    private async Task<IReadOnlyList<BatchAuditEntity>> ReadAuditRowsByBatchAsync(string batchId)
    {
        var context = CreateAuditContext(_fixture.ConnectionString);
        await using (context.ConfigureAwait(false))
        {
            return await context.AuditRecords.Where(record => record.BatchId == batchId).ToListAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reads the audit rows stored for the given audit targets.
    /// </summary>
    /// <param name="targetIds">The audit targets to read.</param>
    /// <returns>The stored audit rows.</returns>
    private async Task<IReadOnlyList<BatchAuditEntity>> ReadAuditRowsAsync(string[] targetIds)
    {
        var context = CreateAuditContext(_fixture.ConnectionString);
        await using (context.ConfigureAwait(false))
        {
            return await context.AuditRecords.Where(record => targetIds.Contains(record.TargetId)).ToListAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts how many times the test handlers actually ran.
    /// </summary>
    public sealed class BatchHandlerCounter
    {
        /// <summary>
        ///     The order identifiers recorded by handler runs.
        /// </summary>
        private readonly ConcurrentBag<Guid> _executions = [];

        /// <summary>
        ///     Records one handler run.
        /// </summary>
        /// <param name="orderId">The order identifier the handler ran for.</param>
        public void Record(Guid orderId)
        {
            _executions.Add(orderId);
        }

        /// <summary>
        ///     Counts the handler runs recorded for one order identifier.
        /// </summary>
        /// <param name="orderId">The order identifier to count.</param>
        /// <returns>The number of recorded runs.</returns>
        public int CountFor(Guid orderId)
        {
            return _executions.Count(executed => executed == orderId);
        }
    }

    /// <summary>
    ///     One batch order row written by the test handlers.
    /// </summary>
    public sealed class BatchOrderEntity
    {
        /// <summary>
        ///     Gets or sets the order identifier.
        /// </summary>
        public Guid OrderId { get; set; }

        /// <summary>
        ///     Gets or sets the order total.
        /// </summary>
        public decimal Total { get; set; }
    }

    /// <summary>
    ///     One audit row written by the Entity Framework Core audit trail.
    /// </summary>
    public sealed class BatchAuditEntity
    {
        /// <summary>
        ///     Gets or sets the audit row identifier.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the audited action.
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the audit outcome as its <see cref="AuditOutcome" /> value.
        /// </summary>
        public int Outcome { get; set; }

        /// <summary>
        ///     Gets or sets the stable failure code for a non-success outcome.
        /// </summary>
        public string? FailureCode { get; set; }

        /// <summary>
        ///     Gets or sets the batch identifier carried by the record properties.
        /// </summary>
        public string? BatchId { get; set; }

        /// <summary>
        ///     Gets or sets the identifier of the resource acted on.
        /// </summary>
        public string? TargetId { get; set; }

        /// <summary>
        ///     Gets or sets the CLR type name of the message that produced the record.
        /// </summary>
        public string? MessageType { get; set; }

        /// <summary>
        ///     Gets or sets the time the action occurred.
        /// </summary>
        public DateTimeOffset RecordedAt { get; set; }
    }

    /// <summary>
    ///     The integration event the order handler stages into the transactional outbox.
    /// </summary>
    public sealed record BatchOrderSubmittedEvent
    {
        /// <summary>
        ///     Gets the order identifier the event carries.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     The Entity Framework Core context the batch handlers stage domain and outbox rows through.
    /// </summary>
    public sealed class BatchDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     The outbox store options used for schema mapping.
        /// </summary>
        private readonly EntityFrameworkCoreOutboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BatchDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The outbox store options.</param>
        public BatchDbContext(DbContextOptions<BatchDbContext> options, EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        /// <summary>
        ///     Gets the batch orders tracked by the context.
        /// </summary>
        public DbSet<BatchOrderEntity> Orders => Set<BatchOrderEntity>();

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);

            modelBuilder.Entity<BatchOrderEntity>(entity =>
            {
                entity.ToTable(DomainTableName, _storeOptions.SchemaName);
                entity.HasKey(order => order.OrderId);
                entity.Property(order => order.OrderId).HasColumnName("order_id");
                entity.Property(order => order.Total).HasColumnName("total").HasColumnType("numeric");
            });
        }
    }

    /// <summary>
    ///     The Entity Framework Core context the audit trail writes its rows through.
    /// </summary>
    public sealed class BatchAuditDbContext : DbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="BatchAuditDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public BatchAuditDbContext(DbContextOptions<BatchAuditDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        ///     Gets the audit records tracked by the context.
        /// </summary>
        public DbSet<BatchAuditEntity> AuditRecords => Set<BatchAuditEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BatchAuditEntity>(entity =>
            {
                entity.ToTable(AuditTableName, EfCoreProviderTestInfrastructure.SchemaName);
                entity.HasKey(record => record.Id);
                entity.Property(record => record.Id).HasColumnName("id");
                entity.Property(record => record.Action).HasColumnName("action").IsRequired();
                entity.Property(record => record.Outcome).HasColumnName("outcome");
                entity.Property(record => record.FailureCode).HasColumnName("failure_code");
                entity.Property(record => record.BatchId).HasColumnName("batch_id");
                entity.Property(record => record.TargetId).HasColumnName("target_id");
                entity.Property(record => record.MessageType).HasColumnName("message_type");
                entity.Property(record => record.RecordedAt).HasColumnName("recorded_at");
            });
        }
    }

    /// <summary>
    ///     Commits the scoped batch context as the mediation unit of work.
    /// </summary>
    public sealed class BatchOrderUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
    {
        /// <summary>
        ///     The scoped context the handlers staged their rows on.
        /// </summary>
        private readonly BatchDbContext _context;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BatchOrderUnitOfWork" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handlers staged their rows on.</param>
        public BatchOrderUnitOfWork(BatchDbContext context)
        {
            _context = context;
        }

        /// <inheritdoc />
        public async Task BeginAsync(CancellationToken cancellationToken = default)
        {
            await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_context.Database.CurrentTransaction is not null)
            {
                await _context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
            }

            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    ///     Writes audit records to their own Entity Framework Core table, outside the batch transaction.
    /// </summary>
    public sealed class EfCoreBatchAuditTrail : IAuditTrail
    {
        /// <summary>
        ///     Creates the independent audit contexts the trail saves each record through.
        /// </summary>
        private readonly IDbContextFactory<BatchAuditDbContext> _factory;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EfCoreBatchAuditTrail" /> class.
        /// </summary>
        /// <param name="factory">Creates the independent audit contexts the trail saves each record through.</param>
        public EfCoreBatchAuditTrail(IDbContextFactory<BatchAuditDbContext> factory)
        {
            _factory = factory;
        }

        /// <inheritdoc />
        public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.AuditRecords.Add(new BatchAuditEntity
                {
                    Id = Guid.NewGuid(),
                    Action = record.Action,
                    Outcome = (int) record.Outcome,
                    FailureCode = record.FailureCode,
                    BatchId = record.Properties.TryGetValue(BatchIdPropertyName, out var batchId) ? batchId : null,
                    TargetId = record.TargetId,
                    MessageType = record.MessageType,
                    RecordedAt = record.OccurredAt
                });

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     An audited, idempotent command that stages its order row and its outbox event for the batch unit of work.
    /// </summary>
    /// <param name="OrderId">The order identifier, used as the idempotency key and the audit target.</param>
    /// <param name="Total">The order total written to the domain row.</param>
    /// <param name="Blocked">Whether the guard denies the command.</param>
    [Audited(ApplyOrderAction)]
    public sealed record ApplyBatchOrderCommand(Guid OrderId, decimal Total, bool Blocked) : ICommand;

    /// <summary>
    ///     Stages the order row and its outbox event, recording each run on the shared counter.
    /// </summary>
    public sealed class ApplyBatchOrderCommandHandler : ICommandHandler<ApplyBatchOrderCommand>
    {
        /// <summary>
        ///     The scoped context the handler stages its row on.
        /// </summary>
        private readonly BatchDbContext _context;

        /// <summary>
        ///     The transactional outbox the handler stages its event through.
        /// </summary>
        private readonly ITransactionalOutbox<BatchDbContext> _outbox;

        /// <summary>
        ///     Collects the audit target only the handler knows.
        /// </summary>
        private readonly IAuditScope _audit;

        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly BatchHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ApplyBatchOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler stages its row on.</param>
        /// <param name="outbox">The transactional outbox the handler stages its event through.</param>
        /// <param name="audit">Collects the audit target only the handler knows.</param>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public ApplyBatchOrderCommandHandler(
            BatchDbContext context,
            ITransactionalOutbox<BatchDbContext> outbox,
            IAuditScope audit,
            BatchHandlerCounter counter)
        {
            _context = context;
            _outbox = outbox;
            _audit = audit;
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAsync(ApplyBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            _counter.Record(message.OrderId);
            _audit.WithTarget(message.OrderId.ToString("N"));
            _context.Orders.Add(new BatchOrderEntity { OrderId = message.OrderId, Total = message.Total });

            await _outbox.EnqueueAsync(
                OutboxEnqueueItem<BatchOrderSubmittedEvent>.From(new BatchOrderSubmittedEvent { OrderId = message.OrderId }),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Denies the order command when the message marks it blocked.
    /// </summary>
    public sealed class ApplyBatchOrderGuard : ICommandGuard<ApplyBatchOrderCommand>
    {
        /// <summary>
        ///     Decides whether the message is permitted to proceed.
        /// </summary>
        /// <param name="message">The message being mediated.</param>
        /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
        /// <returns>The verdict that denies a blocked order and permits every other order.</returns>
        public Task<Verdict> DecideAsync(ApplyBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(message.Blocked
                ? Verdict.Deny("The order is blocked for the command batch test.")
                : Verdict.Allow);
        }
    }

    /// <summary>
    ///     Declares the order command idempotent on its order identifier.
    /// </summary>
    public sealed class ApplyBatchOrderCommandDefinition : IIdempotencyDefinition<ApplyBatchOrderCommand>
    {
        /// <summary>
        ///     Gets the idempotency declaration for the command.
        /// </summary>
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<ApplyBatchOrderCommand>(command => command.OrderId.ToString("N"))
                with
            { Scope = IdempotencyScope };
    }

    /// <summary>
    ///     A command the test shortcut always answers, so its handler never runs.
    /// </summary>
    /// <param name="OrderId">The order identifier the handler would have staged.</param>
    public sealed record AnswerBatchOrderCommand(Guid OrderId) : ICommand;

    /// <summary>
    ///     Would stage the order row, but never runs because the shortcut answers first.
    /// </summary>
    public sealed class AnswerBatchOrderCommandHandler : ICommandHandler<AnswerBatchOrderCommand>
    {
        /// <summary>
        ///     The scoped context the handler would stage its row on.
        /// </summary>
        private readonly BatchDbContext _context;

        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly BatchHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnswerBatchOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler would stage its row on.</param>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public AnswerBatchOrderCommandHandler(BatchDbContext context, BatchHandlerCounter counter)
        {
            _context = context;
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task HandleAsync(AnswerBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            _counter.Record(message.OrderId);
            _context.Orders.Add(new BatchOrderEntity { OrderId = message.OrderId, Total = 1m });

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Answers the order command because its work has already been applied.
    /// </summary>
    public sealed class AnswerBatchOrderShortcut : ICommandShortcut<AnswerBatchOrderCommand>
    {
        /// <summary>
        ///     Answers the message without running its handler.
        /// </summary>
        /// <param name="message">The message being mediated.</param>
        /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
        /// <returns>The shortcut that answers the message.</returns>
        public Task<Shortcut> TryAnswerAsync(AnswerBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Shortcut.Answer("The order was already applied for the command batch test."));
        }
    }

    /// <summary>
    ///     A command whose handler starts another batch, which the batch mediator refuses.
    /// </summary>
    /// <param name="OrderId">The order identifier carried for row assertions.</param>
    public sealed record NestedBatchOrderCommand(Guid OrderId) : ICommand;

    /// <summary>
    ///     Starts a nested batch, which throws before any nested command runs.
    /// </summary>
    public sealed class NestedBatchOrderCommandHandler : ICommandHandler<NestedBatchOrderCommand>
    {
        /// <summary>
        ///     Sends the nested batch that the mediator refuses.
        /// </summary>
        private readonly ICommandBatchMediator _batches;

        /// <summary>
        ///     Initializes a new instance of the <see cref="NestedBatchOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="batches">Sends the nested batch that the mediator refuses.</param>
        public NestedBatchOrderCommandHandler(ICommandBatchMediator batches)
        {
            _batches = batches;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAsync(NestedBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ICommand> nested = [new ApplyBatchOrderCommand(Guid.NewGuid(), 1m, Blocked: false)];

            await _batches.SendManyAsync(nested, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The stored state of one idempotency key observed during a batch test.
    /// </summary>
    /// <param name="Key">The stored key.</param>
    /// <param name="IsCompleted">Whether the key was marked applied.</param>
    /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
    public sealed record StoredIdempotencyKey(string Key, bool IsCompleted, string? Payload);
}
