using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Verifies domain state and inbox rows commit or roll back together through
///     <see cref="PostgreSqlInboxStoreRegistration.CreateTransactionalStore" />.
/// </summary>
public sealed class PostgreSqlInboxTransactionalIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxTransactionalIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlInboxTransactionalIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a rolled-back transaction removes both domain and inbox rows.
    /// </summary>
    [Fact]
    public async Task CreateTransactionalStore_ShouldRollbackDomainAndInboxTogether()
    {
        var (inboxOptions, ordersTableName) = await CreateTablesAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();
        var envelope = CreateEnvelope();
        var registration = new PostgreSqlInboxStoreRegistration(_fixture.DataSource, inboxOptions);

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var transaction = await connection.BeginTransactionAsync().ConfigureAwait(true);
         await using (transaction.ConfigureAwait(false))
         {
        var transactionalStore = registration.CreateTransactionalStore(connection, transaction);

        await InsertOrderAsync(connection, transaction, inboxOptions.SchemaName, ordersTableName, orderId, 10m).ConfigureAwait(true);


        await transactionalStore.AddAsync(envelope).ConfigureAwait(true);
        await transaction.RollbackAsync().ConfigureAwait(true);

        (await CountOrdersAsync(inboxOptions.SchemaName, ordersTableName, orderId))
            .Should().Be(0);

        (await CountInboxMessagesAsync(inboxOptions, envelope.Id))
            .Should().Be(0);
        }
        }
    }

    /// <summary>
    ///     Confirms a committed transaction persists both domain and inbox rows.
    /// </summary>
    [Fact]
    public async Task CreateTransactionalStore_ShouldCommitDomainAndInboxTogether()
    {
        var (inboxOptions, ordersTableName) = await CreateTablesAsync().ConfigureAwait(true);
        var orderId = Guid.NewGuid();
        var envelope = CreateEnvelope();
        var registration = new PostgreSqlInboxStoreRegistration(_fixture.DataSource, inboxOptions);

         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(true);
         await using (connection.ConfigureAwait(true))
         {
         var transaction = await connection.BeginTransactionAsync().ConfigureAwait(true);
         await using (transaction.ConfigureAwait(false))
         {
        var transactionalStore = registration.CreateTransactionalStore(connection, transaction);

        await InsertOrderAsync(connection, transaction, inboxOptions.SchemaName, ordersTableName, orderId, 25m).ConfigureAwait(true);


        await transactionalStore.AddAsync(envelope).ConfigureAwait(true);
        await transaction.CommitAsync().ConfigureAwait(true);

        (await CountOrdersAsync(inboxOptions.SchemaName, ordersTableName, orderId))
            .Should().Be(1);

        (await CountInboxMessagesAsync(inboxOptions, envelope.Id))
            .Should().Be(1);
        }
        }
    }

    /// <summary>
    ///     Creates inbox and domain tables for one test run.
    /// </summary>
    /// <returns>The inbox options and domain table name.</returns>
    private async Task<(PostgreSqlInboxStoreOptions InboxStoreOptions, string OrdersTableName)> CreateTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions($"inbox_pg_tx_{suffix}");
        var ordersTableName = $"orders_pg_inbox_tx_{suffix}";

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions).ConfigureAwait(false);


         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {

        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS "{inboxOptions.SchemaName}"."{ordersTableName}" (
                 order_id uuid NOT NULL PRIMARY KEY,
                 amount numeric NOT NULL);
             """;

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        return (inboxOptions, ordersTableName);
        }
        }
    }

    /// <summary>
    ///     Inserts one domain order row inside the caller transaction.
    /// </summary>
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
    ///     Counts persisted inbox messages after the test transaction completes.
    /// </summary>
    private async Task<int> CountInboxMessagesAsync(PostgreSqlInboxStoreOptions options, Guid messageId)
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
    ///     Creates a sample inbox envelope.
    /// </summary>
    private static InboxEnvelope CreateEnvelope()
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.submit",
            ContractVersion = 1,
            Payload = """{"orderId":"123"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        };
    }
}
