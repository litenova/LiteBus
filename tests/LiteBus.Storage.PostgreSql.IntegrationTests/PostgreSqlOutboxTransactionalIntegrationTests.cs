using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Verifies domain state and outbox rows commit or roll back together through
///     <see cref="PostgreSqlOutboxStore.UseExistingConnection(NpgsqlConnection, NpgsqlTransaction)" />.
/// </summary>
public sealed class PostgreSqlOutboxTransactionalIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxTransactionalIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlOutboxTransactionalIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a rolled-back transaction removes both domain and outbox rows.
    /// </summary>
    [Fact]
    public async Task UseExistingConnection_ShouldRollbackDomainAndOutboxTogether()
    {
        var (outboxOptions, ordersTableName) = await CreateTablesAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();
        var envelope = CreateEnvelope();
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, outboxOptions);

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var transaction = await connection.BeginTransactionAsync().ConfigureAwait(true);
         await using (transaction.ConfigureAwait(false))
         {
        var transactionalStore = store.UseExistingConnection(connection, transaction);

        await InsertOrderAsync(connection, transaction, outboxOptions.SchemaName, ordersTableName, orderId, 10m).ConfigureAwait(true);


        await transactionalStore.AddAsync(envelope).ConfigureAwait(true);
        await transaction.RollbackAsync().ConfigureAwait(true);

        (await CountOrdersAsync(outboxOptions.SchemaName, ordersTableName, orderId))
            .Should().Be(0);

        (await CountOutboxMessagesAsync(outboxOptions, envelope.Id))
            .Should().Be(0);
        }
        }
    }

    /// <summary>
    ///     Confirms a committed transaction persists both domain and outbox rows.
    /// </summary>
    [Fact]
    public async Task UseExistingConnection_ShouldCommitDomainAndOutboxTogether()
    {
        var (outboxOptions, ordersTableName) = await CreateTablesAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();
        var envelope = CreateEnvelope();
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, outboxOptions);

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var transaction = await connection.BeginTransactionAsync().ConfigureAwait(true);
         await using (transaction.ConfigureAwait(false))
         {
        var transactionalStore = store.UseExistingConnection(connection, transaction);

        await InsertOrderAsync(connection, transaction, outboxOptions.SchemaName, ordersTableName, orderId, 25m).ConfigureAwait(true);


        await transactionalStore.AddAsync(envelope).ConfigureAwait(true);
        await transaction.CommitAsync().ConfigureAwait(true);

        (await CountOrdersAsync(outboxOptions.SchemaName, ordersTableName, orderId))
            .Should().Be(1);

        (await CountOutboxMessagesAsync(outboxOptions, envelope.Id))
            .Should().Be(1);
        }
        }
    }

    /// <summary>
    ///     Creates outbox and domain tables for one test run.
    /// </summary>
    /// <returns>The outbox options and domain table name.</returns>
    private async Task<(PostgreSqlOutboxStoreOptions OutboxStoreOptions, string OrdersTableName)> CreateTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions($"outbox_pg_tx_{suffix}");
        var ordersTableName = $"orders_pg_tx_{suffix}";

        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions).ConfigureAwait(false);


         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {

        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS "{outboxOptions.SchemaName}"."{ordersTableName}" (
                 order_id uuid NOT NULL PRIMARY KEY,
                 amount numeric NOT NULL);
             """;

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        return (outboxOptions, ordersTableName);
        }
        }
    }

    /// <summary>
    ///     Inserts one domain order row inside the caller transaction.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The active transaction.</param>
    /// <param name="ordersTableName">The orders table name.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="amount">The order amount.</param>
    /// <returns>A task that represents the asynchronous insert.</returns>
    private static async Task InsertOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schemaName,
        string ordersTableName,
        Guid orderId,
        decimal amount)
    {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {
        command.Transaction = transaction;

        command.CommandText =
            $"""
             INSERT INTO "{schemaName}"."{ordersTableName}" (order_id, amount)
             VALUES (@order_id, @amount);
             """;

        command.Parameters.AddWithValue("order_id", orderId);
        command.Parameters.AddWithValue("amount", amount);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts persisted domain orders after the test transaction completes.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="ordersTableName">The orders table name.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The number of matching rows.</returns>
    private async Task<int> CountOrdersAsync(string schemaName, string ordersTableName, Guid orderId)
    {
         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {

        command.CommandText =
            $"""
             SELECT COUNT(*) FROM "{schemaName}"."{ordersTableName}"
             WHERE order_id = @order_id;
             """;

        command.Parameters.AddWithValue("order_id", orderId);
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
        }
        }
    }

    /// <summary>
    ///     Counts persisted outbox messages after the test transaction completes.
    /// </summary>
    /// <param name="options">The outbox store options.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The number of matching rows.</returns>
    private async Task<int> CountOutboxMessagesAsync(PostgreSqlOutboxStoreOptions options, Guid messageId)
    {
         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {

        command.CommandText =
            $"""
             SELECT COUNT(*) FROM "{options.SchemaName}"."{options.TableName}"
             WHERE message_id = @message_id;
             """;

        command.Parameters.AddWithValue("message_id", messageId);
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
        }
        }
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
            Payload = """{"orderId":"789"}""",
            Topic = "orders",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }
}
