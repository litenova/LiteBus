using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies PostgreSQL schema initialization recovery after advisory lock ownership changes.
/// </summary>
public sealed class PostgreSqlSchemaLockTakeoverTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the shared test data source.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSchemaLockTakeoverTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlSchemaLockTakeoverTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that a waiting initializer takes over when the original advisory lock holder exits without creating
    ///     the schema.
    /// </summary>
    /// <returns>A task that completes when the takeover scenario has been verified.</returns>
    [Fact]
    public async Task EnsureAsync_ShouldAcquireReleasedLockAndCreateSchema()
    {
        var logger = new LockContentionLogger();
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions() with
        {
            Logger = logger
        };

        var lockKey = $"litebus:{PostgreSqlSchemaComponents.Inbox}:{options.SchemaName}:{options.TableName}";

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var lockConnection = await _fixture.DataSource
            .OpenConnectionAsync(cancellationTokenSource.Token)
            .ConfigureAwait(false);
        await using var lockConnectionScope = lockConnection.ConfigureAwait(false);

        var heldLock = await PostgreSqlAdvisoryLockScope.AcquireAsync(
                lockConnection,
                lockKey,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(50),
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        var ensureTask = PostgreSqlInboxSchema.EnsureAsync(
            _fixture.DataSource,
            options,
            cancellationTokenSource.Token);

        try
        {
            await logger.LockContentionObserved
                .WaitAsync(cancellationTokenSource.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            await heldLock.DisposeAsync().ConfigureAwait(false);
        }

        await ensureTask.ConfigureAwait(false);
        await PostgreSqlInboxSchema.ValidateAsync(
                _fixture.DataSource,
                options,
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Signals when schema initialization reports contention on its advisory lock.
    /// </summary>
    private sealed class LockContentionLogger : IPostgreSqlSchemaLogger
    {
        /// <summary>
        ///     Completes when the expected lock contention message is observed.
        /// </summary>
        private readonly TaskCompletionSource _lockContentionObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Gets the task that completes after lock contention is observed.
        /// </summary>
        internal Task LockContentionObserved => _lockContentionObserved.Task;

        /// <inheritdoc />
        public void Log(PostgreSqlSchemaLogLevel level, string message, Exception? exception = null)
        {
            if (message.Contains("is held by another session", StringComparison.Ordinal))
            {
                _lockContentionObserved.TrySetResult();
            }
        }
    }
}
