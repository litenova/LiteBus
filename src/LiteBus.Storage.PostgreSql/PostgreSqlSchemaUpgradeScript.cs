using System;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Describes one version-to-version upgrade script shipped with a LiteBus PostgreSQL store component.
/// </summary>
/// <param name="TargetVersion">
///     The schema version the script produces. The script brings a version <c>TargetVersion - 1</c> table to
///     <c>TargetVersion</c> and is only supported for target versions greater than 1.
/// </param>
/// <param name="RelativePath">
///     The repository-relative path, for example
///     <c>src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v2/upgrade.sql</c>. The same relative content is available in the
///     NuGet package under <c>sql/outbox/v2/upgrade.sql</c>.
/// </param>
/// <param name="BuildScript">
///     The function that renders the upgrade SQL batch for one store table.
/// </param>
internal sealed record PostgreSqlSchemaUpgradeScript(
    int TargetVersion,
    string RelativePath,
    Func<IPostgreSqlStoreTableOptions, string> BuildScript);
