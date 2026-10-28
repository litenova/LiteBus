using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for the PostgreSQL idempotency store: claim and settle behavior, schema bootstrap, the schema probe, and one end-to-end command mediation.
/// </summary>
public sealed class PostgreSqlIdempotencyStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencyStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlIdempotencyStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task ClaimCompleteClaim_ShouldGrantOnceThenReplayTheStoredPayload()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);
        first.ClaimToken.Should().NotBeNullOrWhiteSpace();

        await store.CompleteAsync(key, first.ClaimToken!, """{"receipt":"r-1"}""").ConfigureAwait(false);

        var repeat = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        repeat.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        repeat.Payload.Should().Be("""{"receipt":"r-1"}""");
    }

    [SkippableFact]
    public async Task SecondClaimWhileUnsettled_ShouldAnswerInFlight()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        // A different fingerprint does not matter while the claim is unsettled: the key is held either way.
        var second = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        second.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);
    }

    [SkippableFact]
    public async Task AppliedKey_ShouldMismatchADifferentFingerprintAndReplayAMatch()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        await store.CompleteAsync(key, first.ClaimToken!, """{"receipt":"r-1"}""").ConfigureAwait(false);

        var mismatch = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        mismatch.Outcome.Should().Be(IdempotencyClaimOutcome.FingerprintMismatch);

        var replay = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("""{"receipt":"r-1"}""");
    }

    [SkippableFact]
    public async Task ClaimOlderThanTheTimeout_ShouldBeTakenOver()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        var racing = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        racing.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);

        // A crashed claimant left its claim unsettled; once the timeout passes the next caller takes it over.
        await BackdateClaimAsync(options, key, options.ClaimTimeout.Add(TimeSpan.FromMinutes(1))).ConfigureAwait(false);

        var taken = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        taken.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        // The takeover stored the new caller's fingerprint with the refreshed claim.
        await store.CompleteAsync(key, taken.ClaimToken!, payload: null).ConfigureAwait(false);

        var replay = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);

        var stale = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        stale.Outcome.Should().Be(IdempotencyClaimOutcome.FingerprintMismatch);
    }

    [SkippableFact]
    public async Task Release_ShouldFreeAnUnsettledClaimButNeverAnAppliedKey()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, fingerprint: null).ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        await store.ReleaseAsync(key, first.ClaimToken!).ConfigureAwait(false);

        var retry = await store.TryClaimAsync(key, fingerprint: null).ConfigureAwait(false);
        retry.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        await store.CompleteAsync(key, retry.ClaimToken!, null).ConfigureAwait(false);
        await store.ReleaseAsync(key, retry.ClaimToken!).ConfigureAwait(false);

        var repeat = await store.TryClaimAsync(key, fingerprint: null).ConfigureAwait(false);
        repeat.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        repeat.Payload.Should().BeNull();
    }

    [SkippableFact]
    public async Task LateSettle_AfterTakeover_ShouldLeaveTheNewClaimantAlone()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        await BackdateClaimAsync(options, key, options.ClaimTimeout.Add(TimeSpan.FromMinutes(1))).ConfigureAwait(false);

        var taken = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        taken.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        // The superseded claimant settles late with the token it was granted.
        await store.CompleteAsync(key, first.ClaimToken!, "stale-payload").ConfigureAwait(false);
        await store.ReleaseAsync(key, first.ClaimToken!).ConfigureAwait(false);

        // The new claimant still holds the key: neither marked applied nor freed.
        var racing = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        racing.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);

        await store.CompleteAsync(key, taken.ClaimToken!, "fresh-payload").ConfigureAwait(false);

        var replay = await store.TryClaimAsync(key, "fingerprint-b").ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("fresh-payload");
    }

    [SkippableFact]
    public async Task Complete_OnAMissingKey_ShouldNotInsertAnAppliedRow()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        await store.CompleteAsync(key, Guid.NewGuid().ToString("N"), "payload").ConfigureAwait(false);

        // No applied row was left behind, so the key is still claimable.
        var claim = await store.TryClaimAsync(key, "fingerprint-a").ConfigureAwait(false);
        claim.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldCreateSingleVersion1SchemaWithClaimToken()
    {
        PostgreSqlIdempotencySchema.CurrentSchemaVersion.Should().Be(1);
        PostgreSqlIdempotencySchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlIdempotencySchemaSqlPaths.V1Create,
            PostgreSqlIdempotencySchemaSqlPaths.V1EnsureIndexes);

        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlIdempotencySchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var hasClaimToken = await HasColumnAsync(options, "claim_token").ConfigureAwait(false);
        hasClaimToken.Should().BeTrue();

        var recordedVersion = await ReadRecordedVersionAsync(options).ConfigureAwait(false);
        recordedVersion.Should().Be(1);
    }

    [SkippableFact]
    public void Constructor_WithNonPositiveClaimTimeout_ShouldThrow()
    {
        var zero = () => new PostgreSqlIdempotencyStore(
            _fixture.DataSource,
            new PostgreSqlIdempotencyStoreOptions { ClaimTimeout = TimeSpan.Zero });

        zero.Should().Throw<ArgumentOutOfRangeException>();

        var negative = () => new PostgreSqlIdempotencyStore(
            _fixture.DataSource,
            new PostgreSqlIdempotencyStoreOptions { ClaimTimeout = TimeSpan.FromMinutes(-1) });

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [SkippableFact]
    public async Task ConcurrentClaims_ShouldGrantExactlyOneAndReportTheOtherInFlight()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlIdempotencyStore(_fixture.DataSource, options);
        var key = $"idem:{Guid.NewGuid():N}";

        var outcomes = await Task.WhenAll(
            CaptureClaimAsync(store, key),
            CaptureClaimAsync(store, key)).ConfigureAwait(false);

        outcomes.Should().ContainSingle(outcome => outcome == "granted");
        outcomes.Should().ContainSingle(outcome => outcome == "in-flight");
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = CreateOptions();

        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlIdempotencySchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        var options = CreateOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options))
            .ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        await PostgreSqlIdempotencySchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ValidateAsync_ShouldThrowDriftWhenTableIsMissing()
    {
        var options = CreateOptions();

        var action = async () => await PostgreSqlIdempotencySchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlIdempotencySchema.ComponentName).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportHealthyAfterSchemaIsCreated()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var check = new PostgreSqlIdempotencySchemaDiagnosticCheck(
            new PostgreSqlIdempotencyStoreRegistration(_fixture.DataSource, options));

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
    }

    [SkippableFact]
    public async Task DiagnosticCheck_ShouldReportUnhealthyWhenTableIsMissing()
    {
        var options = CreateOptions();

        var check = new PostgreSqlIdempotencySchemaDiagnosticCheck(
            new PostgreSqlIdempotencyStoreRegistration(_fixture.DataSource, options));

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
    }

    [SkippableFact]
    public async Task SendAsync_ShouldRunTheHandlerOnceAndReplayTheStoredResult()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var counter = new IdempotencyHandlerCounter();

        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<SettleTestPaymentCommand>();
                builder.Register<SettleTestPaymentCommandHandler>();
                builder.Register<SettleTestPaymentCommandDefinition>();
                builder.EnableIdempotency();
                builder.UsePostgreSqlIdempotencyStore(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(options);
                    pg.DisableSchemaInitialization();
                });
            });
        });

        var provider = services.BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var first = await mediator.SendAsync(new SettleTestPaymentCommand("pay-1")).ConfigureAwait(false);
        var second = await mediator.SendAsync(new SettleTestPaymentCommand("pay-1")).ConfigureAwait(false);

        counter.Runs.Should().Be(1);
        first.Should().Be("receipt:pay-1:1");
        second.Should().Be(first);
    }

    [SkippableFact]
    public async Task SchemaInitializer_ShouldCreateSchemaSoTheMediatorCanClaim()
    {
        var options = CreateOptions();
        var counter = new IdempotencyHandlerCounter();

        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<SettleTestPaymentCommand>();
                builder.Register<SettleTestPaymentCommandHandler>();
                builder.Register<SettleTestPaymentCommandDefinition>();
                builder.EnableIdempotency();
                builder.UsePostgreSqlIdempotencyStore(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(options);
                });
            });
        });

        var provider = services.BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);

        var initializer = provider.GetRequiredService<PostgreSqlIdempotencySchemaInitializer>();
        await initializer.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var mediator = provider.GetRequiredService<ICommandMediator>();
        await mediator.SendAsync(new SettleTestPaymentCommand("pay-init")).ConfigureAwait(false);

        counter.Runs.Should().Be(1);
    }

    /// <summary>
    ///     Creates store options against a fresh table in the shared test schema.
    /// </summary>
    /// <returns>The store options.</returns>
    private static PostgreSqlIdempotencyStoreOptions CreateOptions()
    {
        return new PostgreSqlIdempotencyStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"idempotency_{Guid.NewGuid():N}"
        };
    }

    /// <summary>
    ///     Claims one key, translating the outcome into a comparable token.
    /// </summary>
    /// <param name="store">The store under test.</param>
    /// <param name="key">The key to claim.</param>
    /// <returns><c>granted</c> when the claim was granted; otherwise, <c>in-flight</c> or <c>completed</c>.</returns>
    private static async Task<string> CaptureClaimAsync(PostgreSqlIdempotencyStore store, string key)
    {
        var claim = await store.TryClaimAsync(key, fingerprint: null).ConfigureAwait(false);

        return claim.Outcome switch
        {
            IdempotencyClaimOutcome.Granted => "granted",
            IdempotencyClaimOutcome.InFlight => "in-flight",
            _ => "completed"
        };
    }

    /// <summary>
    ///     Moves an unsettled claim back in time, so the takeover path is deterministic without waiting out the timeout.
    /// </summary>
    /// <param name="options">The store options identifying the table.</param>
    /// <param name="key">The key whose claim is backdated.</param>
    /// <param name="age">How old the claim should read.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    private async Task BackdateClaimAsync(PostgreSqlIdempotencyStoreOptions options, string key, TimeSpan age)
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var configuredConnection = connection.ConfigureAwait(false);

        var command = new NpgsqlCommand(
            $"UPDATE \"{options.SchemaName}\".\"{options.TableName}\" "
            + "SET claimed_at = now() - @age WHERE idempotency_key = @key",
            connection);
        await using var configuredCommand = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("age", age);
        command.Parameters.AddWithValue("key", key);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Reports whether the idempotency table carries a column.
    /// </summary>
    /// <param name="options">The store options identifying the table.</param>
    /// <param name="columnName">The column name to look for.</param>
    /// <returns><see langword="true" /> when the column exists; otherwise, <see langword="false" />.</returns>
    private async Task<bool> HasColumnAsync(PostgreSqlIdempotencyStoreOptions options, string columnName)
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var configuredConnection = connection.ConfigureAwait(false);

        var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns "
            + "WHERE table_schema = @schema AND table_name = @table AND column_name = @column",
            connection);
        await using var configuredCommand = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("schema", options.SchemaName);
        command.Parameters.AddWithValue("table", options.TableName);
        command.Parameters.AddWithValue("column", columnName);

        var count = await command.ExecuteScalarAsync().ConfigureAwait(false);

        return count is long rows && rows > 0;
    }

    /// <summary>
    ///     Reads the recorded schema version for the idempotency table.
    /// </summary>
    /// <param name="options">The store options identifying the table.</param>
    /// <returns>The recorded version, or <see langword="null" /> when no version row exists.</returns>
    private async Task<int?> ReadRecordedVersionAsync(PostgreSqlIdempotencyStoreOptions options)
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var configuredConnection = connection.ConfigureAwait(false);

        var command = new NpgsqlCommand(
            $"SELECT version FROM {PostgreSqlIdentifier.Qualify(options.MetadataSchemaName, options.MetadataTableName)} "
            + "WHERE component = @component AND schema_name = @schemaName AND table_name = @tableName",
            connection);
        await using var configuredCommand = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("component", PostgreSqlIdempotencySchema.ComponentName);
        command.Parameters.AddWithValue("schemaName", options.SchemaName);
        command.Parameters.AddWithValue("tableName", options.TableName);

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);

        return result is int version ? version : null;
    }

    /// <summary>
    ///     Counts how many times the test handler actually ran.
    /// </summary>
    public sealed class IdempotencyHandlerCounter
    {
        /// <summary>
        ///     The number of handler runs recorded so far.
        /// </summary>
        private int _runs;

        /// <summary>
        ///     Gets the number of handler runs recorded so far.
        /// </summary>
        public int Runs => _runs;

        /// <summary>
        ///     Records one handler run.
        /// </summary>
        /// <returns>The one-based run number.</returns>
        public int NextRun()
        {
            return Interlocked.Increment(ref _runs);
        }
    }

    /// <summary>
    ///     A result-producing command that declares its payment as the idempotency key.
    /// </summary>
    /// <param name="PaymentId">The payment identifier used as the idempotency key.</param>
    public sealed record SettleTestPaymentCommand(string PaymentId) : ICommand<string>;

    /// <summary>
    ///     Settles the test payment, recording each run on the shared counter.
    /// </summary>
    public sealed class SettleTestPaymentCommandHandler : ICommandHandler<SettleTestPaymentCommand, string>
    {
        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly IdempotencyHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SettleTestPaymentCommandHandler" /> class.
        /// </summary>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public SettleTestPaymentCommandHandler(IdempotencyHandlerCounter counter)
        {
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt, which differs per run so a replay is distinguishable from a rerun.</returns>
        public Task<string> HandleAsync(SettleTestPaymentCommand message, CancellationToken cancellationToken = default)
        {
            var run = _counter.NextRun();
            return Task.FromResult($"receipt:{message.PaymentId}:{run}");
        }
    }

    /// <summary>
    ///     Declares the test payment command idempotent with result replay.
    /// </summary>
    public sealed class SettleTestPaymentCommandDefinition : IIdempotencyDefinition<SettleTestPaymentCommand>
    {
        /// <summary>
        ///     Gets the idempotency declaration for the command.
        /// </summary>
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<SettleTestPaymentCommand>(command => command.PaymentId) with { ReplayResult = true };
    }
}
