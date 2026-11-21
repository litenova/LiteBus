using LiteBus.Commands;
using LiteBus.Commands.Recurring;
using LiteBus.Commands.Recurring.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL recurring occurrence store: recording, latest-occurrence reads, occurrence-key uniqueness across writers and tenants, tenant and loop isolation, pooler-friendly connections, schema bootstrap, and the schema probe.
/// </summary>
public sealed class PostgreSqlRecurringOccurrenceStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringOccurrenceStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlRecurringOccurrenceStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task TryRecordAsync_GetLastAsync_ShouldRoundTripTheOccurrence()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var occurrence = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);

        var recorded = await store.TryRecordAsync(occurrence, CancellationToken.None).ConfigureAwait(false);
        recorded.Should().BeTrue();

        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);
        last.Should().NotBeNull();
        last.Should().Be(occurrence);
    }

    [SkippableFact]
    public async Task TryRecordAsync_WhenKeyExists_ShouldReturnFalseAndKeepTheFirstRow()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var first = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);
        var second = first with { ScheduledAt = PostgreSqlTestInfrastructure.BaseTime.AddDays(1) };

        var firstRecorded = await store.TryRecordAsync(first, CancellationToken.None).ConfigureAwait(false);
        var secondRecorded = await store.TryRecordAsync(second, CancellationToken.None).ConfigureAwait(false);

        firstRecorded.Should().BeTrue();
        secondRecorded.Should().BeFalse();

        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);
        last.Should().Be(first);
    }

    [SkippableFact]
    public async Task TryRecordAsync_WhenKeyRepeatsForAnotherTenant_ShouldReturnFalse()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);

        var firstRecorded = await store.TryRecordAsync(
            CreateOccurrence("close-carts", "acme", "shared-key", PostgreSqlTestInfrastructure.BaseTime),
            CancellationToken.None).ConfigureAwait(false);
        var secondRecorded = await store.TryRecordAsync(
            CreateOccurrence("close-carts", "globex", "shared-key", PostgreSqlTestInfrastructure.BaseTime),
            CancellationToken.None).ConfigureAwait(false);

        firstRecorded.Should().BeTrue();
        secondRecorded.Should().BeFalse();
    }

    [SkippableFact]
    public async Task GetLastAsync_WhenNothingRecorded_ShouldReturnNull()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);

        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);

        last.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetLastAsync_WithSeveralOccurrences_ShouldReturnTheLatestScheduled()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var earliest = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-17", PostgreSqlTestInfrastructure.BaseTime.AddDays(-2));
        var middle = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-18", PostgreSqlTestInfrastructure.BaseTime.AddDays(-1));
        var latest = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);

        // Recorded out of order so the read proves ordering, not insertion order.
        (await store.TryRecordAsync(middle, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await store.TryRecordAsync(latest, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await store.TryRecordAsync(earliest, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();

        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);

        last.Should().Be(latest);
    }

    [SkippableFact]
    public async Task GetLastAsync_ShouldIsolateTenants()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var firstTenant = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);
        var secondTenant = CreateOccurrence("close-carts", "globex", "close-carts/globex/2026-11-19", PostgreSqlTestInfrastructure.BaseTime.AddHours(1));
        var unscoped = CreateOccurrence("close-carts", null, "close-carts/2026-11-19", PostgreSqlTestInfrastructure.BaseTime.AddHours(2));

        (await store.TryRecordAsync(firstTenant, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await store.TryRecordAsync(secondTenant, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await store.TryRecordAsync(unscoped, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();

        (await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false)).Should().Be(firstTenant);
        (await store.GetLastAsync("close-carts", "globex", CancellationToken.None).ConfigureAwait(false)).Should().Be(secondTenant);
        (await store.GetLastAsync("close-carts", null, CancellationToken.None).ConfigureAwait(false)).Should().Be(unscoped);
    }

    [SkippableFact]
    public async Task GetLastAsync_ShouldIsolateLoops()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var firstLoop = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);
        var secondLoop = CreateOccurrence("send-reminders", "acme", "send-reminders/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime.AddHours(1));

        (await store.TryRecordAsync(firstLoop, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await store.TryRecordAsync(secondLoop, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();

        (await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false)).Should().Be(firstLoop);
        (await store.GetLastAsync("send-reminders", "acme", CancellationToken.None).ConfigureAwait(false)).Should().Be(secondLoop);
    }

    [SkippableFact]
    public async Task TryRecordAsync_WhenTwoWritersRace_ShouldRecordExactlyOnce()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var firstStore = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var secondStore = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var occurrence = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);

        var results = await Task.WhenAll(
            firstStore.TryRecordAsync(occurrence, CancellationToken.None).AsTask(),
            secondStore.TryRecordAsync(occurrence, CancellationToken.None).AsTask()).ConfigureAwait(false);

        results.Count(recorded => recorded).Should().Be(1);

        var last = await firstStore.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);
        last.Should().Be(occurrence);
    }

    [SkippableFact]
    public async Task TryRecordAsync_WithUnpooledAndPooledSources_ShouldCoordinate()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var unpooledSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString)
        {
            ConnectionStringBuilder = { Pooling = false }
        }.Build();

        await using var configuredSource = unpooledSource.ConfigureAwait(false);

        var unpooledStore = new PostgreSqlRecurringOccurrenceStore(unpooledSource, options);
        var pooledStore = new PostgreSqlRecurringOccurrenceStore(_fixture.DataSource, options);
        var occurrence = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);

        (await unpooledStore.TryRecordAsync(occurrence, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await pooledStore.TryRecordAsync(occurrence, CancellationToken.None).ConfigureAwait(false)).Should().BeFalse();

        (await pooledStore.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false)).Should().Be(occurrence);
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = CreateOptions();

        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlRecurringOccurrenceSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ValidateAsync_ShouldThrowDriftWhenTableIsMissing()
    {
        var options = CreateOptions();

        var action = async () => await PostgreSqlRecurringOccurrenceSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlRecurringOccurrenceSchema.ComponentName).ConfigureAwait(false);
    }

    [SkippableFact]
    public void GetCreateScript_ShouldRenderOccurrenceTableObjects()
    {
        var options = CreateOptions();
        var script = PostgreSqlRecurringOccurrenceSchema.GetCreateScript(options);

        script.Should().Contain(options.OccurrenceTableName);
        script.Should().Contain("occurrence_key");
        script.Should().Contain("scheduled_at");
        script.Should().Contain(options.MetadataTableName);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportHealthyAfterSchemaIsCreated()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringOccurrenceSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var check = new PostgreSqlRecurringOccurrenceSchemaDiagnosticCheck(
            new PostgreSqlRecurringLeaseRegistration(_fixture.DataSource, options));

        check.Name.Should().Be("recurring.postgresql.occurrences");

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportUnhealthyWhenTableIsMissing()
    {
        var options = CreateOptions();

        var check = new PostgreSqlRecurringOccurrenceSchemaDiagnosticCheck(
            new PostgreSqlRecurringLeaseRegistration(_fixture.DataSource, options));

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
    }

    [SkippableFact]
    public async Task SchemaInitializer_ShouldCreateSchemaSoTheStoreCanRecord()
    {
        var options = CreateOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.UsePostgreSqlRecurringLease(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(options);
                });
            });
        });

        var provider = services.BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);

        var leaseInitializer = provider.GetRequiredService<PostgreSqlRecurringLeaseSchemaInitializer>();
        await leaseInitializer.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var occurrenceInitializer = provider.GetRequiredService<PostgreSqlRecurringOccurrenceSchemaInitializer>();
        await occurrenceInitializer.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var store = provider.GetRequiredService<IRecurringOccurrenceStore>();
        var occurrence = CreateOccurrence("close-carts", "acme", "close-carts/acme/2026-11-19", PostgreSqlTestInfrastructure.BaseTime);

        (await store.TryRecordAsync(occurrence, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false)).Should().Be(occurrence);
    }

    private static PostgreSqlRecurringLeaseOptions CreateOptions()
    {
        return new PostgreSqlRecurringLeaseOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"recurring_leases_{Guid.NewGuid():N}",
            OccurrenceTableName = $"recurring_occ_{Guid.NewGuid():N}"
        };
    }

    private static RecurringOccurrence CreateOccurrence(string loopName, string? tenantId, string occurrenceKey, DateTimeOffset scheduledAt)
    {
        return new RecurringOccurrence(loopName, tenantId, occurrenceKey, scheduledAt, scheduledAt.AddMinutes(1));
    }
}
