using LiteBus.Auditing;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL audit schema: bootstrap idempotence, validation, drift reporting, the canonical script,
///     the schema probe, and host startup initialization.
/// </summary>
public sealed class PostgreSqlAuditSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditSchemaTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlAuditSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = CreateOptions();

        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlAuditSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ValidateAsync_ShouldThrowDriftWhenTablesAreMissing()
    {
        var options = CreateOptions();

        var validate = async () =>
        {
            await PostgreSqlAuditSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
        };

        await validate.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlAuditSchema.ComponentName).ConfigureAwait(false);
    }

    [SkippableFact]
    public void GetCreateScript_ShouldRenderRecordsHeadsAndCheckpointsTables()
    {
        var options = CreateOptions();
        var script = PostgreSqlAuditSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain(options.HeadsTableName);
        script.Should().Contain(options.CheckpointsTableName);
        script.Should().Contain("document_key");
        script.Should().Contain("staged_order");
        script.Should().Contain("last_checkpoint_sequence");
        script.Should().Contain("publish_state");
        script.Should().Contain(options.MetadataTableName);
    }

    [SkippableFact]
    public void SqlFiles_ShouldNameTheCanonicalScripts()
    {
        PostgreSqlAuditSchema.SqlFiles.Should().HaveCount(2);
        PostgreSqlAuditSchema.SqlFiles.Should().Contain(file => file.RelativePath.EndsWith("create.sql"));
        PostgreSqlAuditSchema.SqlFiles.Should().Contain(file => file.RelativePath.EndsWith("ensure_indexes.sql"));
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportHealthyAfterSchemaIsCreated()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var check = new PostgreSqlAuditSchemaDiagnosticCheck(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));

        check.Name.Should().Be("audit.postgresql.schema");

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportUnhealthyWhenTablesAreMissing()
    {
        var options = CreateOptions();

        var check = new PostgreSqlAuditSchemaDiagnosticCheck(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
    }

    [SkippableFact]
    public async Task SchemaInitializer_ShouldCreateSchemaSoTheStoreCanStage()
    {
        var options = CreateOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddAuditing(auditing => auditing
                .ForCommands()
                .UsePostgreSqlStore(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(options);
                }));
        });

        var provider = services.BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);

        var initializer = provider.GetRequiredService<PostgreSqlAuditSchemaInitializer>();
        await initializer.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var store = new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));

        await store.StageAsync("orders", CreateRecord(), CancellationToken.None).ConfigureAwait(false);

        var unlinked = await CollectAsync(
            store.ListUnlinkedAsync("orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        unlinked.Should().ContainSingle();
    }

    private static PostgreSqlAuditStoreOptions CreateOptions()
    {
        var suffix = Guid.NewGuid().ToString("N");

        return new PostgreSqlAuditStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"audit_records_{suffix}",
            HeadsTableName = $"audit_heads_{suffix}",
            CheckpointsTableName = $"audit_checkpoints_{suffix}"
        };
    }

    private static PostgreSqlAuditRecord CreateRecord()
    {
        return new PostgreSqlAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = "orders.place-order",
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime
        };
    }

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        List<T> items = [];
        var enumerator = source.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                items.Add(enumerator.Current);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        return items;
    }
}
