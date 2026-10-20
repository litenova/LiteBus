using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies that the schema-owned outbox insert statement writes the exact row the PostgreSQL store reads back.
/// </summary>
public sealed class PostgreSqlOutboxInsertStatementTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxInsertStatementTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlOutboxInsertStatementTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task CreateInsertStatement_WhenExecuted_InsertsRowTheStoreReadsBackWithEveryFieldEqual()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.insert-statement",
            ContractVersion = 2,
            Payload = """{"orderId":"order-1"}""",
            Topic = "orders.events",
            CreatedAt = now,
            VisibleAfter = now.AddMinutes(1),
            Status = OutboxStatus.Failed,
            AttemptCount = 2,
            LeaseOwner = "worker-1",
            LeaseExpiresAt = now.AddMinutes(5),
            LastError = "boom",
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1",
            IdempotencyKey = "key-1",
            // Keys in the order jsonb stores them, so the round trip compares the value rather than its key order.
            TraceContext = """{"span_id":"00f067aa0ba902b7","trace_id":"4bf92f3577b34da6a3ff9024b"}""",
            LeaseGeneration = 4
        };

        var statement = PostgreSqlOutboxSchema.CreateInsertStatement(envelope, options);
        statement.Values.Should().HaveCount(18);

        await ExecuteInsertAsync(statement).ConfigureAwait(false);

        var stored = await QueryOutboxAsync(store, envelope.Id).ConfigureAwait(false);
        stored.Id.Should().Be(envelope.Id);
        stored.ContractName.Should().Be(envelope.ContractName);
        stored.ContractVersion.Should().Be(envelope.ContractVersion);
        stored.Payload.Should().Be(envelope.Payload);
        stored.Topic.Should().Be(envelope.Topic);
        stored.CreatedAt.Should().Be(envelope.CreatedAt);
        stored.VisibleAfter.Should().Be(envelope.VisibleAfter);
        stored.Status.Should().Be(envelope.Status);
        stored.AttemptCount.Should().Be(envelope.AttemptCount);
        stored.LeaseOwner.Should().Be(envelope.LeaseOwner);
        stored.LeaseExpiresAt.Should().Be(envelope.LeaseExpiresAt);
        stored.LastError.Should().Be(envelope.LastError);
        stored.CorrelationId.Should().Be(envelope.CorrelationId);
        stored.CausationId.Should().Be(envelope.CausationId);
        stored.TenantId.Should().Be(envelope.TenantId);
        stored.IdempotencyKey.Should().Be(envelope.IdempotencyKey);
        stored.TraceContext.Should().Be(envelope.TraceContext);
        stored.LeaseGeneration.Should().Be(envelope.LeaseGeneration);
    }

    [SkippableFact]
    public async Task CreateInsertStatement_WhenOptionalFieldsAreNull_RoundTripsWithNormalizedTenant()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.insert-statement",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        };

        var statement = PostgreSqlOutboxSchema.CreateInsertStatement(envelope, options);

        await ExecuteInsertAsync(statement).ConfigureAwait(false);

        var stored = await QueryOutboxAsync(store, envelope.Id).ConfigureAwait(false);
        stored.Id.Should().Be(envelope.Id);
        stored.ContractName.Should().Be(envelope.ContractName);
        stored.Payload.Should().Be(envelope.Payload);
        stored.CreatedAt.Should().Be(envelope.CreatedAt);
        stored.Topic.Should().BeNull();
        stored.VisibleAfter.Should().BeNull();
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.LastError.Should().BeNull();
        stored.CorrelationId.Should().BeNull();
        stored.CausationId.Should().BeNull();
        stored.IdempotencyKey.Should().BeNull();
        stored.TraceContext.Should().BeNull();

        // The store normalizes a missing tenant to the empty unscoped value on write, exactly like AddAsync.
        stored.TenantId.Should().Be(string.Empty);
    }

    [SkippableFact]
    public void FormatSql_WhenPlaceholderIsQuestionMark_ReplacesEveryPositionalMarker()
    {
        var statement = PostgreSqlOutboxSchema.CreateInsertStatement(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.insert-statement",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        statement.Sql.Should().Contain("$17::jsonb");
        statement.Sql.Should().Contain("ON CONFLICT DO NOTHING");
        statement.Sql.Should().NotContain("RETURNING");

        var formatted = statement.FormatSql('?');
        formatted.Should().NotContain("$");
        formatted.Should().Contain("?::jsonb");
        formatted.Count(character => character == '?').Should().Be(statement.Values.Count);
    }

    private async Task ExecuteInsertAsync(PostgreSqlOutboxInsertStatement statement)
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();

            await using (command.ConfigureAwait(false))
            {
                command.CommandText = statement.Sql;

                // Npgsql binds $1..$n only to unnamed parameters, in order.
                foreach (var value in statement.Values)
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = value });
                }

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<OutboxEnvelope> QueryOutboxAsync(PostgreSqlOutboxStore store, Guid messageId)
    {
        var page = await store.QueryAsync(
            new OutboxMessageFilter { MessageId = messageId },
            new OutboxMessagePageRequest { PageSize = 1 }).ConfigureAwait(false);

        page.Items.Should().ContainSingle();
        return page.Items[0];
    }
}
