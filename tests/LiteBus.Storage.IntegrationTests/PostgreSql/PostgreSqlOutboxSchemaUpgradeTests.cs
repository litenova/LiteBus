using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for automatic PostgreSQL outbox schema upgrades from version 1 to version 2.
/// </summary>
public sealed class PostgreSqlOutboxSchemaUpgradeTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxSchemaUpgradeTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms startup upgrades a shipped version 1 table to version 2 without losing rows.
    /// </summary>
    [SkippableFact]
    public async Task EnsureAsync_WhenVersion1TableExists_ShouldUpgradeToVersion2AndPreserveRows()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await CreateVersion1TableAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await InsertVersion1RowAsync(_fixture.DataSource, options, firstId, "tests.events.upgrade-first", """{"seeded":1}""").ConfigureAwait(false);
        await InsertVersion1RowAsync(_fixture.DataSource, options, secondId, "tests.events.upgrade-second", """{"seeded":2}""").ConfigureAwait(false);
        await RecordSchemaVersionAsync(_fixture.DataSource, options, 1).ConfigureAwait(false);

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var recordedVersion = await ReadRecordedVersionAsync(_fixture.DataSource, options).ConfigureAwait(false);
        recordedVersion.Should().Be(2);

        var hasHandlerColumn = await HasHandlerColumnAsync(_fixture.DataSource, options).ConfigureAwait(false);
        hasHandlerColumn.Should().BeTrue();

        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);

        var first = await QueryOutboxAsync(store, firstId).ConfigureAwait(false);
        first.ContractName.Should().Be("tests.events.upgrade-first");
        first.Payload.Should().Be("""{"seeded":1}""");
        first.Handler.Should().BeNull();

        var second = await QueryOutboxAsync(store, secondId).ConfigureAwait(false);
        second.ContractName.Should().Be("tests.events.upgrade-second");
        second.Payload.Should().Be("""{"seeded":2}""");
        second.Handler.Should().BeNull();
    }

    /// <summary>
    ///     Confirms the store writes and leases an envelope handler after a version 1 upgrade.
    /// </summary>
    [SkippableFact]
    public async Task LeasePendingAsync_AfterVersion1Upgrade_ShouldReturnEnvelopeHandler()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await CreateVersion1TableAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.handler",
            ContractVersion = 1,
            Payload = "{}",
            Handler = "tests.handlers.order-submitted",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(messageId);
        leased[0].Handler.Should().Be("tests.handlers.order-submitted");
    }

    /// <summary>
    ///     Confirms validate-only startup on a version 1 table reports the versions and the upgrade script.
    /// </summary>
    [SkippableFact]
    public async Task RunAsync_WhenInitializationIsDisabled_ShouldThrowDriftNamingUpgradePath()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions() with
        {
            EnsureSchemaCreationOnStartup = false,
            ValidateSchemaCreationOnStartup = true
        };

        await CreateVersion1TableAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var initializer = new PostgreSqlOutboxSchemaInitializer(
            new PostgreSqlOutboxStoreRegistration(_fixture.DataSource, options));

        var action = async () => await initializer.RunAsync(CancellationToken.None).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Outbox &&
                exception.ExpectedVersion == 2 &&
                exception.ActualVersion == 1 &&
                exception.Details.Contains("version 1", StringComparison.Ordinal) &&
                exception.Details.Contains("version 2", StringComparison.Ordinal) &&
                exception.Details.Contains(PostgreSqlOutboxSchemaSqlPaths.V2Upgrade, StringComparison.Ordinal))
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms a fresh database is created directly at version 2.
    /// </summary>
    [SkippableFact]
    public async Task EnsureAsync_WhenDatabaseIsFresh_ShouldCreateVersion2Directly()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var recordedVersion = await ReadRecordedVersionAsync(_fixture.DataSource, options).ConfigureAwait(false);
        recordedVersion.Should().Be(2);

        var hasHandlerColumn = await HasHandlerColumnAsync(_fixture.DataSource, options).ConfigureAwait(false);
        hasHandlerColumn.Should().BeTrue();

        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    private static async Task<OutboxEnvelope> QueryOutboxAsync(PostgreSqlOutboxStore store, Guid messageId)
    {
        var page = await store.QueryAsync(
            new OutboxMessageFilter { MessageId = messageId },
            new OutboxMessagePageRequest { PageSize = 1 }).ConfigureAwait(false);

        page.Items.Should().ContainSingle();
        return page.Items[0];
    }

    private static async Task CreateVersion1TableAsync(NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions options)
    {
        var script = LoadEmbeddedSql(
            typeof(PostgreSqlOutboxSchema),
            "outbox.v1.create.sql",
            CreateOutboxStoreTokens(options));

        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = script;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task InsertVersion1RowAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions options,
        Guid messageId,
        string contractName,
        string payload)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = $"""
                    INSERT INTO {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)}
                        (message_id, contract_name, contract_version, payload, created_at, status, attempt_count)
                    VALUES (@messageId, @contractName, @contractVersion, @payload, @createdAt, @status, @attemptCount);
                    """;
                command.Parameters.AddWithValue("messageId", messageId);
                command.Parameters.AddWithValue("contractName", contractName);
                command.Parameters.AddWithValue("contractVersion", 1);
                command.Parameters.AddWithValue("payload", payload);
                command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("status", (int) OutboxStatus.Pending);
                command.Parameters.AddWithValue("attemptCount", 0);

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task RecordSchemaVersionAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions options,
        int version)
    {
        var createScript = LoadEmbeddedSql(
            typeof(PostgreSqlSchemaComponents),
            "metadata.create.sql",
            CreateMetadataTokens(options));

        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var createCommand = connection.CreateCommand();
            await using (createCommand.ConfigureAwait(false))
            {
                createCommand.CommandText = createScript;
                await createCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var insertCommand = connection.CreateCommand();
            await using (insertCommand.ConfigureAwait(false))
            {
                insertCommand.CommandText = $"""
                    INSERT INTO {PostgreSqlIdentifier.Qualify(options.MetadataSchemaName, options.MetadataTableName)}
                        (component, schema_name, table_name, version, applied_at)
                    VALUES (@component, @schemaName, @tableName, @version, @appliedAt);
                    """;
                insertCommand.Parameters.AddWithValue("component", PostgreSqlSchemaComponents.Outbox);
                insertCommand.Parameters.AddWithValue("schemaName", options.SchemaName);
                insertCommand.Parameters.AddWithValue("tableName", options.TableName);
                insertCommand.Parameters.AddWithValue("version", version);
                insertCommand.Parameters.AddWithValue("appliedAt", DateTimeOffset.UtcNow);

                await insertCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<int?> ReadRecordedVersionAsync(NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions options)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = $"SELECT version FROM {PostgreSqlIdentifier.Qualify(options.MetadataSchemaName, options.MetadataTableName)} WHERE component = @component AND schema_name = @schemaName AND table_name = @tableName;";
                command.Parameters.AddWithValue("component", PostgreSqlSchemaComponents.Outbox);
                command.Parameters.AddWithValue("schemaName", options.SchemaName);
                command.Parameters.AddWithValue("tableName", options.TableName);

                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return result is int version ? version : null;
            }
        }
    }

    private static async Task<bool> HasHandlerColumnAsync(NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions options)
    {
        var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = @schemaName AND table_name = @tableName AND column_name = 'handler';";
                command.Parameters.AddWithValue("schemaName", options.SchemaName);
                command.Parameters.AddWithValue("tableName", options.TableName);

                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return result is long count && count > 0;
            }
        }
    }

    private static string LoadEmbeddedSql(
        Type anchorType,
        string resourceSuffix,
        IReadOnlyDictionary<string, string> tokens)
    {
        var resourceName = $"{anchorType.Assembly.GetName().Name}.Sql.{resourceSuffix}";

        using var stream = anchorType.Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"embedded SQL resource '{resourceName}' must ship with the package.");

        using var reader = new StreamReader(stream!);
        var template = reader.ReadToEnd();

        var rendered = template;

        foreach (var (token, value) in tokens)
        {
            rendered = rendered.Replace($"{{{{{token}}}}}", value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static IReadOnlyDictionary<string, string> CreateOutboxStoreTokens(PostgreSqlOutboxStoreOptions options)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        tokens["QuotedSchemaName"] = PostgreSqlIdentifier.Quote(options.SchemaName);
        tokens["QualifiedTableName"] = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        tokens["LeaseIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "lease_idx");
        tokens["TopicIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "topic_idx");
        tokens["IdempotencyIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "idempotency_idx");
        tokens["NotifyChannelName"] = PostgreSqlOutboxNotifyChannel.ChannelName;
        tokens["NotifyFunctionName"] = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_fn");
        tokens["NotifyTriggerName"] = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_trg");
        return tokens;
    }

    private static IReadOnlyDictionary<string, string> CreateMetadataTokens(PostgreSqlOutboxStoreOptions options)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        tokens["QuotedMetadataSchemaName"] = PostgreSqlIdentifier.Quote(options.MetadataSchemaName);
        tokens["QualifiedMetadataTableName"] = PostgreSqlIdentifier.Qualify(options.MetadataSchemaName, options.MetadataTableName);
        return tokens;
    }
}
