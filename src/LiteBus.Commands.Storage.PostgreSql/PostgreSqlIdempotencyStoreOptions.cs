using System;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Options for the PostgreSQL idempotency store table and schema bootstrap.
/// </summary>
public sealed record PostgreSqlIdempotencyStoreOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema that contains the idempotency keys table.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the idempotency keys table name.
    /// </summary>
    public string TableName { get; init; } = "litebus_idempotency_keys";

    /// <summary>
    ///     Gets how long an unsettled claim blocks its key before a new caller takes it over.
    /// </summary>
    /// <value>
    ///     The claim timeout, defaulting to five minutes. Must be positive. A claimant that crashes without settling
    ///     would otherwise block its key until an operator intervenes; a claim older than this is refreshed for the new
    ///     caller instead. Keep it comfortably longer than the slowest mediation that claims through this store, so a
    ///     live claimant always settles first.
    /// </value>
    public TimeSpan ClaimTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Validates the configured values before the store is built.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ClaimTimeout" /> is zero or negative.</exception>
    /// <remarks>
    ///     A timeout of zero or less would make every unsettled claim immediately takeover-eligible, so two
    ///     concurrent deliveries of one key would both be granted and both run the handler.
    /// </remarks>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ClaimTimeout, TimeSpan.Zero);
    }

    /// <summary>
    ///     Gets a value indicating whether schema creation runs during host startup.
    /// </summary>
    public bool EnsureSchemaCreationOnStartup { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether schema validation runs during host startup.
    /// </summary>
    public bool ValidateSchemaCreationOnStartup { get; init; }
}
