namespace LiteBus.PostgreSql;

/// <summary>
///     Describes one canonical SQL file shipped with a LiteBus PostgreSQL package.
/// </summary>
/// <param name="RelativePath">
///     The repository-relative path, for example
///     <c>src/LiteBus.Inbox.PostgreSql/Sql/inbox/v1/create.sql</c>. The same relative content is available in the
///     NuGet package under <c>sql/inbox/v1/create.sql</c>.
/// </param>
/// <param name="Purpose">A short description of when to run the script in a migration pipeline.</param>
/// <example>
///     <code>
/// foreach (var file in PostgreSqlInboxSchema.SqlFiles)
/// {
///     Console.WriteLine($"{file.RelativePath}: {file.Purpose}");
/// }
///     </code>
/// </example>
public sealed record PostgreSqlSchemaSqlFile(string RelativePath, string Purpose);
