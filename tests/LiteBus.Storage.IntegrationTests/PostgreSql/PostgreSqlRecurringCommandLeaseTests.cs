using LiteBus.Commands;
using LiteBus.Commands.Recurring;
using LiteBus.Commands.Recurring.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL recurring command lease: mutual exclusion per name, release on dispose and on expiry, background renewal, stolen-lease shutdown, lease-loss signalling, pooler-friendly connections, schema bootstrap, and the schema probe.
/// </summary>
public sealed class PostgreSqlRecurringCommandLeaseTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringCommandLeaseTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlRecurringCommandLeaseTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task TryAcquireAsync_WhenNameIsHeld_ShouldReturnNullUntilReleased()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var firstLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var secondLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var first = await firstLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        first.Should().NotBeNull();

        await using (first!.ConfigureAwait(false))
        {
            var blocked = await secondLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
            blocked.Should().BeNull();
        }

        var third = await secondLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        third.Should().NotBeNull();
        await third!.DisposeAsync().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task TryAcquireAsync_WhenNamesDiffer_ShouldGrantBoth()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var lease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var firstName = "lease-" + Guid.NewGuid();
        var secondName = "lease-" + Guid.NewGuid();

        var first = await lease.TryAcquireAsync(firstName, CancellationToken.None).ConfigureAwait(false);
        first.Should().NotBeNull();

        await using (first!.ConfigureAwait(false))
        {
            var second = await lease.TryAcquireAsync(secondName, CancellationToken.None).ConfigureAwait(false);
            second.Should().NotBeNull();
            await second!.DisposeAsync().ConfigureAwait(false);
        }
    }

    [SkippableFact]
    public async Task TryAcquireAsync_WhenTwoHoldersRace_ShouldGrantExactlyOne()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var firstLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var secondLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var results = await Task.WhenAll(
            firstLease.TryAcquireAsync(name, CancellationToken.None),
            secondLease.TryAcquireAsync(name, CancellationToken.None)).ConfigureAwait(false);

        results.Count(handle => handle is null).Should().Be(1);

        var winner = results.SingleOrDefault(handle => handle is not null);
        winner.Should().NotBeNull();
        await winner!.DisposeAsync().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task TryAcquireAsync_AfterExpiryWithoutRenewal_ShouldGrantLeaseToAnotherHolder()
    {
        // The renewal is parked near the duration so the row can be lapsed with SQL before the first renewal fires.
        // Parking it past the duration is refused by options validation, so natural expiry cannot be awaited here.
        var options = CreateOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(29));
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var firstLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var secondLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var first = await firstLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        first.Should().NotBeNull();

        var firstRow = await ReadLeaseRowAsync(_fixture.DataSource, options, name).ConfigureAwait(false);
        firstRow.Should().NotBeNull();

        await using (first!.ConfigureAwait(false))
        {
            await ExpireLeaseAsync(_fixture.DataSource, options, name).ConfigureAwait(false);

            var second = await secondLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
            second.Should().NotBeNull();

            await using (second!.ConfigureAwait(false))
            {
                var secondRow = await ReadLeaseRowAsync(_fixture.DataSource, options, name).ConfigureAwait(false);
                secondRow.Should().NotBeNull();
                secondRow!.Holder.Should().NotBe(firstRow!.Holder);
            }
        }
    }

    [SkippableFact]
    public async Task TryAcquireAsync_WhenSameHolderReacquires_ShouldReturnNull()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        // One lease instance means one holder identity, so the second acquisition races the first from the same holder.
        var lease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var first = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        first.Should().NotBeNull();

        await using (first!.ConfigureAwait(false))
        {
            var second = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
            second.Should().BeNull();
        }
    }

    [SkippableFact]
    public void LeaseOptions_WhenRenewIntervalIsNotBelowDuration_ShouldThrow()
    {
        var above = () => new PostgreSqlRecurringLeaseOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromMinutes(1)
        }.Validate();

        above.Should().Throw<ArgumentOutOfRangeException>();

        var equal = () => new PostgreSqlRecurringLeaseOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromSeconds(30)
        }.Validate();

        equal.Should().Throw<ArgumentOutOfRangeException>();
    }

    [SkippableFact]
    public async Task SchemaInitializer_ShouldResolveTheDataSourceOwnerSoShutdownDisposesIt()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.UsePostgreSqlRecurringLease(pg =>
                {
                    pg.UseConnectionString("Host=localhost;Database=litebus_owner_probe;Username=postgres");
                });
            });
        });

        var provider = services.BuildServiceProvider();

        // Resolving the initializer is what host startup does; it must pull the owner into existence so container
        // disposal later disposes the module-owned data source even when no loop ever resolves the lease.
        _ = provider.GetRequiredService<PostgreSqlRecurringLeaseSchemaInitializer>();
        var dataSource = provider.GetRequiredService<PostgreSqlRecurringLeaseRegistration>().DataSource;

        await provider.DisposeAsync().ConfigureAwait(false);

        var act = () => dataSource.CreateConnection();

        act.Should().Throw<ObjectDisposedException>();
    }

    [SkippableFact]
    public async Task TryAcquireAsync_WithRenewal_ShouldStayHeldPastOriginalExpiry()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(300));
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var firstLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var secondLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var first = await firstLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        first.Should().NotBeNull();

        var original = await ReadLeaseRowAsync(_fixture.DataSource, options, name).ConfigureAwait(false);
        original.Should().NotBeNull();

        await using (first!.ConfigureAwait(false))
        {
            await WaitUntilDatabaseTimePassesAsync(_fixture.DataSource, original!.ExpiresAt).ConfigureAwait(false);

            var renewed = await ReadLeaseRowAsync(_fixture.DataSource, options, name).ConfigureAwait(false);
            renewed.Should().NotBeNull();
            renewed!.Holder.Should().Be(original.Holder);
            renewed.ExpiresAt.Should().BeAfter(original.ExpiresAt);

            var blocked = await secondLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
            blocked.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task Renewal_AfterLeaseIsStolen_ShouldStopAndWarnOnce()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var logger = new RecordingLogger<PostgreSqlRecurringCommandLease>();
        var lease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options, logger);
        var name = "lease-" + Guid.NewGuid();

        var handle = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        handle.Should().NotBeNull();

        await using (handle!.ConfigureAwait(false))
        {
            await StealLeaseAsync(_fixture.DataSource, options, name, "intruder").ConfigureAwait(false);
            await WaitForWarningAsync(logger).ConfigureAwait(false);

            logger.EventIds.Should().Contain(2110);

            var warningsAfterSteal = logger.WarningCount;
            await Task.Delay(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);
            logger.WarningCount.Should().Be(warningsAfterSteal);
            logger.WarningCount.Should().Be(1);

            var row = await ReadLeaseRowAsync(_fixture.DataSource, options, name).ConfigureAwait(false);
            row.Should().NotBeNull();
            row!.Holder.Should().Be("intruder");
        }
    }

    [SkippableFact]
    public async Task Renewal_WhenTableIsDropped_ShouldStopWithoutResurrectingTheRow()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var logger = new RecordingLogger<PostgreSqlRecurringCommandLease>();
        var lease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options, logger);
        var name = "lease-" + Guid.NewGuid();

        var handle = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        handle.Should().NotBeNull();

        await using (handle!.ConfigureAwait(false))
        {
            await DropTableAsync(_fixture.DataSource, options).ConfigureAwait(false);
            await WaitForWarningAsync(logger).ConfigureAwait(false);

            logger.EventIds.Should().Contain(2111);

            var warningsAfterDrop = logger.WarningCount;

            await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

            logger.WarningCount.Should().Be(warningsAfterDrop);

            var row = await ReadLeaseRowAsync(_fixture.DataSource, options, name).ConfigureAwait(false);
            row.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task LeaseLost_AfterLeaseIsStolen_ShouldCancel()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var lease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var handle = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        handle.Should().NotBeNull();
        handle!.LeaseLost.IsCancellationRequested.Should().BeFalse();

        await using (handle.ConfigureAwait(false))
        {
            await StealLeaseAsync(_fixture.DataSource, options, name, "intruder").ConfigureAwait(false);
            await WaitForLeaseLostAsync(handle).ConfigureAwait(false);

            handle.LeaseLost.IsCancellationRequested.Should().BeTrue();
        }
    }

    [SkippableFact]
    public async Task LeaseLost_WhenRenewalFails_ShouldCancel()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var lease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var handle = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        handle.Should().NotBeNull();
        handle!.LeaseLost.IsCancellationRequested.Should().BeFalse();

        await using (handle.ConfigureAwait(false))
        {
            await DropTableAsync(_fixture.DataSource, options).ConfigureAwait(false);
            await WaitForLeaseLostAsync(handle).ConfigureAwait(false);

            handle.LeaseLost.IsCancellationRequested.Should().BeTrue();

            await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        }
    }

    [SkippableFact]
    public async Task TryAcquireAsync_WithUnpooledAndPooledSources_ShouldCoordinate()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var unpooledSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString)
        {
            ConnectionStringBuilder = { Pooling = false }
        }.Build();

        await using var configuredSource = unpooledSource.ConfigureAwait(false);

        var unpooledLease = new PostgreSqlRecurringCommandLease(unpooledSource, options);
        var pooledLease = new PostgreSqlRecurringCommandLease(_fixture.DataSource, options);
        var name = "lease-" + Guid.NewGuid();

        var first = await unpooledLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        first.Should().NotBeNull();

        await using (first!.ConfigureAwait(false))
        {
            var blocked = await pooledLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
            blocked.Should().BeNull();
        }

        var second = await pooledLease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        second.Should().NotBeNull();
        await second!.DisposeAsync().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = CreateOptions();

        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlRecurringLeaseSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ValidateAsync_ShouldThrowDriftWhenTableIsMissing()
    {
        var options = CreateOptions();

        var action = async () => await PostgreSqlRecurringLeaseSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlRecurringLeaseSchema.ComponentName).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportHealthyAfterSchemaIsCreated()
    {
        var options = CreateOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var check = new PostgreSqlRecurringLeaseSchemaDiagnosticCheck(
            new PostgreSqlRecurringLeaseRegistration(_fixture.DataSource, options));

        check.Name.Should().Be("recurring.postgresql.schema");

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportUnhealthyWhenTableIsMissing()
    {
        var options = CreateOptions();

        var check = new PostgreSqlRecurringLeaseSchemaDiagnosticCheck(
            new PostgreSqlRecurringLeaseRegistration(_fixture.DataSource, options));

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
    }

    [SkippableFact]
    public async Task SchemaInitializer_ShouldCreateSchemaSoTheLeaseCanAcquire()
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

        var initializer = provider.GetRequiredService<PostgreSqlRecurringLeaseSchemaInitializer>();
        await initializer.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var lease = provider.GetRequiredService<IRecurringCommandLease>();
        var name = "lease-" + Guid.NewGuid();

        var handle = await lease.TryAcquireAsync(name, CancellationToken.None).ConfigureAwait(false);
        handle.Should().NotBeNull();
        await handle!.DisposeAsync().ConfigureAwait(false);
    }

    private static PostgreSqlRecurringLeaseOptions CreateOptions(TimeSpan? leaseDuration = null, TimeSpan? renewInterval = null)
    {
        return new PostgreSqlRecurringLeaseOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"recurring_leases_{Guid.NewGuid():N}",
            LeaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30),
            RenewInterval = renewInterval
        };
    }

    private static async Task<LeaseRow?> ReadLeaseRowAsync(NpgsqlDataSource dataSource, PostgreSqlRecurringLeaseOptions options, string leaseName)
    {
        var sql = $"SELECT holder, acquired_at, expires_at, renewed_at FROM {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)} WHERE lease_name = @leaseName;";

        using var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("leaseName", leaseName);
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }

        return new LeaseRow(reader.GetString(0), reader.GetDateTime(1), reader.GetDateTime(2), reader.GetDateTime(3));
    }

    private static async Task WaitUntilDatabaseTimePassesAsync(NpgsqlDataSource dataSource, DateTime instant)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
            using var command = new NpgsqlCommand("SELECT now() > @instant;", connection);
            command.Parameters.AddWithValue("instant", instant);

            if (await command.ExecuteScalarAsync().ConfigureAwait(false) is true)
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException("The database clock did not pass the original expiry within 30 seconds.");
    }

    private static async Task ExpireLeaseAsync(NpgsqlDataSource dataSource, PostgreSqlRecurringLeaseOptions options, string leaseName)
    {
        var sql = $"UPDATE {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)} SET expires_at = now() - interval '1 second' WHERE lease_name = @leaseName;";

        using var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("leaseName", leaseName);

        if (await command.ExecuteNonQueryAsync().ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException($"Lease '{leaseName}' has no row to expire.");
        }
    }

    private static async Task StealLeaseAsync(NpgsqlDataSource dataSource, PostgreSqlRecurringLeaseOptions options, string leaseName, string holder)
    {
        var sql = $"UPDATE {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)} SET holder = @holder, acquired_at = now(), expires_at = now() + @extra, renewed_at = now() WHERE lease_name = @leaseName;";

        using var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("holder", holder);
        command.Parameters.AddWithValue("extra", TimeSpan.FromSeconds(60));
        command.Parameters.AddWithValue("leaseName", leaseName);

        if (await command.ExecuteNonQueryAsync().ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException($"Lease '{leaseName}' has no row to steal.");
        }
    }

    private static async Task DropTableAsync(NpgsqlDataSource dataSource, PostgreSqlRecurringLeaseOptions options)
    {
        var sql = $"DROP TABLE {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)};";

        using var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WaitForWarningAsync(RecordingLogger<PostgreSqlRecurringCommandLease> logger)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (logger.WarningCount > 0)
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new TimeoutException("No lease renewal warning was logged within 30 seconds.");
    }

    private static async Task WaitForLeaseLostAsync(IRecurringCommandLeaseHandle handle)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (handle.LeaseLost.IsCancellationRequested)
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new TimeoutException("The lease-loss token was not cancelled within 30 seconds.");
    }

    private sealed record LeaseRow(string Holder, DateTime AcquiredAt, DateTime ExpiresAt, DateTime RenewedAt);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public int WarningCount
        {
            get
            {
                lock (_entries)
                {
                    return _entries.Count(entry => entry.Level == LogLevel.Warning);
                }
            }
        }

        public IReadOnlyList<int> EventIds
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries.Select(entry => entry.EventId.Id)];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, eventId));
            }
        }

        private sealed record LogEntry(LogLevel Level, EventId EventId);
    }
}
