using Microsoft.Data.Sqlite;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Provides an isolated file-backed SQLite database for one storage contract test class.
/// </summary>
public sealed class SqliteDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    ///     The database file deleted when the fixture is disposed.
    /// </summary>
    private string? _databasePath;

    /// <summary>
    ///     Gets the connection string used by independent store contexts.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"litebus-storage-{Guid.NewGuid():N}.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
            Pooling = false
        }.ToString();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (_databasePath is not null && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }
}
