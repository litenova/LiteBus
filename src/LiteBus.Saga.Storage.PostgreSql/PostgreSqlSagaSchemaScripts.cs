using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Builds saga schema SQL scripts from embedded resources.
/// </summary>
internal static class PostgreSqlSagaSchemaScripts
{
    /// <summary>
    ///     The assembly that embeds saga schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlSagaSchemaScripts).Assembly;

    /// <summary>
    ///     Gets the canonical saga SQL files shipped with this package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles =>
    [
        new("src/LiteBus.Saga.Storage.PostgreSql/Sql/saga/v1/create.sql", "Creates saga_instances table schema version 1.")
    ];

    /// <summary>
    ///     Returns the rendered create script for the current saga schema.
    /// </summary>
    /// <param name="options">The saga store options.</param>
    /// <returns>The create script.</returns>
    internal static string GetCreateScript(PostgreSqlSagaStoreOptions? options = null)
    {
        options ??= new PostgreSqlSagaStoreOptions();
        var tokens = BuildTokens(options);
        return RenderEmbedded("LiteBus.Saga.Storage.PostgreSql.Sql.saga.v1.create.sql", tokens);
    }

    /// <summary>
    ///     Builds placeholder tokens for one options instance.
    /// </summary>
    /// <param name="options">The saga store options.</param>
    /// <returns>The token map.</returns>
    private static Dictionary<string, string> BuildTokens(PostgreSqlSagaStoreOptions options)
    {
        var qualifiedTableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        var quotedSchemaName = PostgreSqlIdentifier.Quote(options.SchemaName);
        var completedIndexName = PostgreSqlIdentifier.IndexName(options.TableName, "completed_idx");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["QuotedSchemaName"] = quotedSchemaName,
            ["QualifiedTableName"] = qualifiedTableName,
            ["CompletedIndexName"] = completedIndexName
        };
    }

    /// <summary>
    ///     Renders one embedded SQL resource with token replacement.
    /// </summary>
    /// <param name="resourceName">The embedded resource name.</param>
    /// <param name="tokens">The replacement tokens.</param>
    /// <returns>The rendered SQL script.</returns>
    private static string RenderEmbedded(string resourceName, IReadOnlyDictionary<string, string> tokens)
    {
        using var stream = Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded saga SQL resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sql = reader.ReadToEnd();

        foreach (var (token, value) in tokens)
        {
            sql = sql.Replace($"{{{{{token}}}}}", value, StringComparison.Ordinal);
        }

        return sql;
    }
}