using System;
using System.Collections.Generic;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Describes the SQL scripts and version metadata for one LiteBus PostgreSQL store component.
/// </summary>
internal sealed class PostgreSqlComponentSchemaDefinition
{
    /// <summary>
    ///     Gets the component name stored in schema metadata.
    /// </summary>
    public required string Component { get; init; }

    /// <summary>
    ///     Gets the schema version implemented by this package release.
    /// </summary>
    public required int CurrentSchemaVersion { get; init; }

    /// <summary>
    ///     Gets the ordered column groups introduced by each schema version.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<string>> VersionColumnSets { get; init; }

    /// <summary>
    ///     Gets the canonical SQL files shipped with the component package.
    /// </summary>
    public required IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; init; }

    /// <summary>
    ///     Gets the function that builds the version 1 create script.
    /// </summary>
    public required Func<IPostgreSqlStoreTableOptions, string> BuildVersion1CreateScript { get; init; }

    /// <summary>
    ///     Gets the function that builds an incremental upgrade script.
    /// </summary>
    public required Func<IPostgreSqlStoreTableOptions, int, int, string> BuildUpgradeScript { get; init; }

    /// <summary>
    ///     Gets the function that ensures indexes exist for the current schema version.
    /// </summary>
    public required Func<IPostgreSqlStoreTableOptions, string> BuildEnsureIndexesScript { get; init; }

    /// <summary>
    ///     Gets the function that builds the full create script for one schema version.
    /// </summary>
    public required Func<IPostgreSqlStoreTableOptions, int, string> BuildCreateScript { get; init; }

    /// <summary>
    ///     Gets the function that creates the advisory lock key for one store table.
    /// </summary>
    public required Func<IPostgreSqlStoreTableOptions, string> CreateLockKey { get; init; }
}
