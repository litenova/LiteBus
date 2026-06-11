using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Verifies non-EF transactional writers against PostgreSQL with manual bind and ambient provider registration.
/// </summary>
public sealed class PostgreSqlTransactionalWritersIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlTransactionalWritersIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlTransactionalWritersIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms <see cref="ITransactionalOutbox" /> enqueues through a manually bound store in the caller transaction.
    /// </summary>
    [Fact]
    public async Task TransactionalOutbox_manual_bind_should_commit_with_domain()
    {
        var (outboxOptions, ordersTableName, registration) = await CreateOutboxTablesAsync().ConfigureAwait(false);
        var orderId = Guid.NewGuid();
        var factory = CreateOutboxEnvelopeFactory();

        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var store = registration.CreateTransactionalStore(connection, transaction);
        var writer = new StoreBoundTransactionalOutbox(store, factory, TimeProvider.System);

        await InsertOrderAsync(connection, transaction, outboxOptions.SchemaName, ordersTableName, orderId, 15m)
            .ConfigureAwait(false);
        var receipt = await writer.EnqueueAsync(new TestIntegrationEvent { OrderId = orderId }).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);

        (await CountOrdersAsync(outboxOptions.SchemaName, ordersTableName, orderId).ConfigureAwait(false))
            .Should().Be(1);
        (await CountOutboxMessagesAsync(outboxOptions, receipt.Id).ConfigureAwait(false))
            .Should().Be(1);
    }

    /// <summary>
    ///     Confirms inbox and outbox rows in one transaction commit or roll back together.
    /// </summary>
    [Fact]
    public async Task ManualBind_inbox_and_outbox_should_roll_back_together()
    {
        var tables = await CreateCombinedTablesAsync().ConfigureAwait(false);
        var orderId = Guid.NewGuid();
        var inboxFactory = CreateInboxEnvelopeFactory();
        var outboxFactory = CreateOutboxEnvelopeFactory();

        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var inboxStore = tables.InboxRegistration.CreateTransactionalStore(connection, transaction);
        var outboxStore = tables.OutboxRegistration.CreateTransactionalStore(connection, transaction);
        var inbox = new StoreBoundTransactionalInbox(inboxStore, inboxFactory, TimeProvider.System);
        var outbox = new StoreBoundTransactionalOutbox(outboxStore, outboxFactory, TimeProvider.System);

        await InsertOrderAsync(
                connection,
                transaction,
                tables.InboxOptions.SchemaName,
                tables.OrdersTableName,
                orderId,
                5m)
            .ConfigureAwait(false);
        var inboxReceipt = await inbox.AcceptAsync(new TestCommand { OrderId = orderId }).ConfigureAwait(false);
        var outboxReceipt = await outbox.EnqueueAsync(new TestIntegrationEvent { OrderId = orderId }).ConfigureAwait(false);
        await transaction.RollbackAsync().ConfigureAwait(false);

        (await CountOrdersAsync(tables.InboxOptions.SchemaName, tables.OrdersTableName, orderId).ConfigureAwait(false))
            .Should().Be(0);
        (await CountInboxMessagesAsync(tables.InboxOptions, inboxReceipt.Id).ConfigureAwait(false))
            .Should().Be(0);
        (await CountOutboxMessagesAsync(tables.OutboxOptions, outboxReceipt.Id).ConfigureAwait(false))
            .Should().Be(0);
    }

    /// <summary>
    ///     Confirms ambient provider registration resolves scoped transactional outbox inside an active transaction.
    /// </summary>
    [Fact]
    public async Task AmbientProvider_should_enqueue_outbox_in_active_transaction()
    {
        var (outboxOptions, ordersTableName, _) = await CreateOutboxTablesAsync().ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        services.AddScoped<IPostgreSqlTransactionProvider, ScopedTransactionProvider>();
        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddOutboxModule(outbox =>
            {
                outbox.Contracts.Register<TestIntegrationEvent>("orders.events.submitted", 1);
                outbox.UsePostgreSqlStorage(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(outboxOptions);
                    pg.DisableSchemaInitialization();
                    pg.EnableAmbientTransactionProvider();
                });
            });
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var ambient = scope.ServiceProvider.GetRequiredService<IPostgreSqlTransactionProvider>() as ScopedTransactionProvider;
        ambient!.Activate(await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false));

        await InsertOrderAsync(
                ambient.Connection!,
                ambient.Transaction!,
                outboxOptions.SchemaName,
                ordersTableName,
                orderId,
                99m)
            .ConfigureAwait(false);

        var writer = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
        var receipt = await writer.EnqueueAsync(new TestIntegrationEvent { OrderId = orderId }).ConfigureAwait(false);
        await ambient.Transaction!.CommitAsync().ConfigureAwait(false);

        (await CountOrdersAsync(outboxOptions.SchemaName, ordersTableName, orderId).ConfigureAwait(false))
            .Should().Be(1);
        (await CountOutboxMessagesAsync(outboxOptions, receipt.Id).ConfigureAwait(false))
            .Should().Be(1);
    }

    /// <summary>
    ///     Creates outbox tables and registration for one test run.
    /// </summary>
    private async Task<(PostgreSqlOutboxStoreOptions OutboxOptions, string OrdersTableName, PostgreSqlOutboxStoreRegistration Registration)> CreateOutboxTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxOptions($"outbox_tx_writer_{suffix}");
        var ordersTableName = $"orders_tx_writer_{suffix}";

        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions)
            .ConfigureAwait(false);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS "{outboxOptions.SchemaName}"."{ordersTableName}" (
                 order_id uuid NOT NULL PRIMARY KEY,
                 amount numeric NOT NULL);
             """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        var registration = new PostgreSqlOutboxStoreRegistration(_fixture.DataSource, outboxOptions);
        return (outboxOptions, ordersTableName, registration);
    }

    /// <summary>
    ///     Creates inbox and outbox tables for combined transactional tests.
    /// </summary>
    private async Task<CombinedTables> CreateCombinedTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxOptions($"inbox_combined_{suffix}");
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxOptions($"outbox_combined_{suffix}");
        var ordersTableName = $"orders_combined_{suffix}";

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions)
            .ConfigureAwait(false);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions)
            .ConfigureAwait(false);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS "{inboxOptions.SchemaName}"."{ordersTableName}" (
                 order_id uuid NOT NULL PRIMARY KEY,
                 amount numeric NOT NULL);
             """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        return new CombinedTables(
            inboxOptions,
            outboxOptions,
            ordersTableName,
            new PostgreSqlInboxStoreRegistration(_fixture.DataSource, inboxOptions),
            new PostgreSqlOutboxStoreRegistration(_fixture.DataSource, outboxOptions));
    }

    /// <summary>
    ///     Creates an inbox envelope factory for integration tests.
    /// </summary>
    private static InboxEnvelopeFactory CreateInboxEnvelopeFactory()
    {
        var registry = new MessageContractRegistry();
        registry.Register<TestCommand>("orders.commands.submit", 1);
        return new InboxEnvelopeFactory(registry, new SystemTextJsonMessageSerializer(), TimeProvider.System);
    }

    /// <summary>
    ///     Creates an outbox envelope factory for integration tests.
    /// </summary>
    private static OutboxEnvelopeFactory CreateOutboxEnvelopeFactory()
    {
        var registry = new MessageContractRegistry();
        registry.Register<TestIntegrationEvent>("orders.events.submitted", 1);
        return new OutboxEnvelopeFactory(registry, new SystemTextJsonMessageSerializer(), TimeProvider.System);
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
        await using var command = connection.CreateCommand();
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

    /// <summary>
    ///     Counts persisted domain orders.
    /// </summary>
    private async Task<int> CountOrdersAsync(string schemaName, string ordersTableName, Guid orderId)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT COUNT(*) FROM "{schemaName}"."{ordersTableName}"
             WHERE order_id = @order_id;
             """;
        command.Parameters.AddWithValue("order_id", orderId);
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }

    /// <summary>
    ///     Counts persisted inbox messages.
    /// </summary>
    private async Task<int> CountInboxMessagesAsync(PostgreSqlInboxStoreOptions options, Guid messageId)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT COUNT(*) FROM "{options.SchemaName}"."{options.TableName}"
             WHERE message_id = @message_id;
             """;
        command.Parameters.AddWithValue("message_id", messageId);
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }

    /// <summary>
    ///     Counts persisted outbox messages.
    /// </summary>
    private async Task<int> CountOutboxMessagesAsync(PostgreSqlOutboxStoreOptions options, Guid messageId)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT COUNT(*) FROM "{options.SchemaName}"."{options.TableName}"
             WHERE message_id = @message_id;
             """;
        command.Parameters.AddWithValue("message_id", messageId);
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }

    /// <summary>
    ///     Test command accepted into the inbox.
    /// </summary>
    private sealed record TestCommand
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Test integration event enqueued to the outbox.
    /// </summary>
    private sealed record TestIntegrationEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Table metadata for combined inbox and outbox tests.
    /// </summary>
    /// <param name="InboxOptions">The inbox store options.</param>
    /// <param name="OutboxOptions">The outbox store options.</param>
    /// <param name="OrdersTableName">The shared domain table name.</param>
    /// <param name="InboxRegistration">The inbox store registration.</param>
    /// <param name="OutboxRegistration">The outbox store registration.</param>
    private sealed record CombinedTables(
        PostgreSqlInboxStoreOptions InboxOptions,
        PostgreSqlOutboxStoreOptions OutboxOptions,
        string OrdersTableName,
        PostgreSqlInboxStoreRegistration InboxRegistration,
        PostgreSqlOutboxStoreRegistration OutboxRegistration);

    /// <summary>
    ///     Scoped provider activated per test scope.
    /// </summary>
    private sealed class ScopedTransactionProvider : IPostgreSqlTransactionProvider, IAsyncDisposable
    {
        /// <summary>
        ///     Gets the active connection when activated.
        /// </summary>
        public NpgsqlConnection? Connection { get; private set; }

        /// <summary>
        ///     Gets the active transaction when activated.
        /// </summary>
        public NpgsqlTransaction? Transaction { get; private set; }

        /// <summary>
        ///     Opens a transaction on the supplied connection and marks it active for the scope.
        /// </summary>
        /// <param name="connection">The open connection owned by the test scope.</param>
        public async void Activate(NpgsqlConnection connection)
        {
            Connection = connection;
            Transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public bool TryGetCurrent(out NpgsqlConnection? connection, out NpgsqlTransaction? transaction)
        {
            connection = Connection;
            transaction = Transaction;
            return Connection is not null && Transaction is not null;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Transaction is not null)
            {
                await Transaction.DisposeAsync().ConfigureAwait(false);
            }

            if (Connection is not null)
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
