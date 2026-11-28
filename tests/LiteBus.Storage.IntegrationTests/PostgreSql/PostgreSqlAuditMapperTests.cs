using System.Diagnostics.CodeAnalysis;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies that the PostgreSQL audit trail awaits the record mapper's asynchronous form on the connection and
///     transaction the record is written on: a row the mapper writes commits with a success record, rolls back with it,
///     and survives the business rollback with an out-of-band denial record.
/// </summary>
public sealed class PostgreSqlAuditMapperTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The shared PostgreSQL container fixture.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditMapperTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlAuditMapperTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Success_record_commits_the_mappers_row_with_the_business_transaction()
    {
        var (options, pseudonyms) = await CreateSchemaAsync().ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider, pseudonyms);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(Record(action, AuditOutcome.Succeeded)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.CommitAsync().ConfigureAwait(false);

        (await CountPseudonymsAsync(pseudonyms, action).ConfigureAwait(false)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Success_record_rolls_the_mappers_row_back_with_the_business_transaction()
    {
        var (options, pseudonyms) = await CreateSchemaAsync().ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider, pseudonyms);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(Record(action, AuditOutcome.Succeeded)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.RollbackAsync().ConfigureAwait(false);

        (await CountPseudonymsAsync(pseudonyms, action).ConfigureAwait(false)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Denied_record_keeps_the_mappers_row_through_the_business_rollback()
    {
        var (options, pseudonyms) = await CreateSchemaAsync().ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider, pseudonyms);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(Record(action, AuditOutcome.Denied)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.RollbackAsync().ConfigureAwait(false);

        (await CountPseudonymsAsync(pseudonyms, action).ConfigureAwait(false)).Should().Be(1);
    }

    /// <summary>
    ///     Creates the audit tables and the mapper's pseudonym table under fresh names.
    /// </summary>
    /// <returns>The store options and the qualified pseudonym table name.</returns>
    private async Task<(PostgreSqlAuditStoreOptions Options, string Pseudonyms)> CreateSchemaAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var options = new PostgreSqlAuditStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"audit_records_{suffix}",
            HeadsTableName = $"audit_heads_{suffix}",
            CheckpointsTableName = $"audit_checkpoints_{suffix}"
        };

        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var pseudonyms = $"\"{PostgreSqlTestInfrastructure.TestSchemaName}\".\"audit_pseudonyms_{suffix}\"";

        using var command = _fixture.DataSource.CreateCommand(
            $"CREATE TABLE {pseudonyms} (id uuid PRIMARY KEY, action text NOT NULL);");
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        return (options, pseudonyms);
    }

    /// <summary>
    ///     Builds the trail with the pseudonymizing mapper.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <param name="provider">The ambient transaction provider.</param>
    /// <param name="pseudonyms">The qualified pseudonym table name.</param>
    /// <returns>The trail.</returns>
    private PostgreSqlAuditTrail<PostgreSqlAuditRecord> CreateTrail(
        PostgreSqlAuditStoreOptions options,
        IPostgreSqlTransactionProvider provider,
        string pseudonyms)
    {
        return new PostgreSqlAuditTrail<PostgreSqlAuditRecord>(
            new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
                new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options)),
            new PseudonymizingMapper(pseudonyms),
            new PostgreSqlAuditRecordSegmenter(),
            new ServiceCollection().AddSingleton<IPostgreSqlTransactionProvider>(provider).BuildServiceProvider());
    }

    /// <summary>
    ///     Counts the pseudonym rows written for an action.
    /// </summary>
    /// <param name="pseudonyms">The qualified pseudonym table name.</param>
    /// <param name="action">The audit action.</param>
    /// <returns>The row count.</returns>
    private async Task<long> CountPseudonymsAsync(string pseudonyms, string action)
    {
        using var command = _fixture.DataSource.CreateCommand(
            $"SELECT count(*) FROM {pseudonyms} WHERE action = @action;");
        command.Parameters.AddWithValue("action", action);

        return (long) (await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    /// <summary>
    ///     Builds an audit record with an outcome.
    /// </summary>
    /// <param name="action">The audit action.</param>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The record.</returns>
    private static AuditRecord Record(string action, AuditOutcome outcome)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = outcome,
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime
        };
    }

    /// <summary>
    ///     A mapper that writes a pseudonym row through the store's connection and transaction.
    /// </summary>
    private sealed class PseudonymizingMapper : IAuditRecordMapper<PostgreSqlAuditRecord>
    {
        /// <summary>
        ///     The qualified pseudonym table name.
        /// </summary>
        private readonly string _pseudonyms;

        /// <summary>
        ///     The store's default mapper.
        /// </summary>
        private readonly PostgreSqlAuditRecordMapper _inner = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="PseudonymizingMapper" /> class.
        /// </summary>
        /// <param name="pseudonyms">The qualified pseudonym table name.</param>
        public PseudonymizingMapper(string pseudonyms)
        {
            _pseudonyms = pseudonyms;
        }

        /// <inheritdoc />
        public async ValueTask<PostgreSqlAuditRecord> MapAsync(
            AuditRecord record,
            AuditMappingContext context,
            CancellationToken cancellationToken)
        {
            var connection = context.Features.Get<NpgsqlConnection>();
            context.Features.TryGet<NpgsqlTransaction>(out var transaction);

            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO {_pseudonyms} (id, action) VALUES (@id, @action);";
            command.Transaction = transaction;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("action", record.Action);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return _inner.Map(record);
        }
    }

    /// <summary>
    ///     An ambient transaction provider the test activates around each write.
    /// </summary>
    private sealed class TestTransactionProvider : IPostgreSqlTransactionProvider
    {
        /// <summary>
        ///     The active connection, or <see langword="null" />.
        /// </summary>
        private NpgsqlConnection? _connection;

        /// <summary>
        ///     The active transaction, or <see langword="null" />.
        /// </summary>
        private NpgsqlTransaction? _transaction;

        /// <summary>
        ///     Makes a connection and transaction ambient.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        public void Activate(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        /// <summary>
        ///     Clears the ambient connection and transaction.
        /// </summary>
        public void Deactivate()
        {
            _connection = null;
            _transaction = null;
        }

        /// <inheritdoc />
        public bool TryGetCurrent(
            [NotNullWhen(true)] out NpgsqlConnection? connection,
            [NotNullWhen(true)] out NpgsqlTransaction? transaction)
        {
            connection = _connection;
            transaction = _transaction;

            return connection is not null && transaction is not null;
        }
    }
}
