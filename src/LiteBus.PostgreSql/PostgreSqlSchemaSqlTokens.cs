using System.Collections.Generic;

namespace LiteBus.PostgreSql;

/// <summary>
///     Builds placeholder token maps for embedded SQL templates.
/// </summary>
internal static class PostgreSqlSchemaSqlTokens
{
    /// <summary>
    ///     Builds placeholder tokens for metadata table SQL templates.
    /// </summary>
    /// <param name="options">The store options that supply metadata schema and table names.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    internal static Dictionary<string, string> ForMetadata(IPostgreSqlStoreTableOptions options)
    {
        return new Dictionary<string, string>
        {
            ["QuotedMetadataSchemaName"] = PostgreSqlIdentifier.Quote(options.MetadataSchemaName),
            ["QualifiedMetadataTableName"] = PostgreSqlIdentifier.Qualify(
                options.MetadataSchemaName,
                options.MetadataTableName)
        };
    }

    /// <summary>
    ///     Builds placeholder tokens for inbox or outbox store table SQL templates.
    /// </summary>
    /// <param name="options">The store options that supply schema and table names.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    internal static Dictionary<string, string> ForStoreTable(IPostgreSqlStoreTableOptions options)
    {
        return new Dictionary<string, string>
        {
            ["QuotedSchemaName"] = PostgreSqlIdentifier.Quote(options.SchemaName),
            ["QualifiedTableName"] = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)
        };
    }

    /// <summary>
    ///     Combines two token maps, with values from <paramref name="second" /> overriding duplicates.
    /// </summary>
    /// <param name="first">The first token map.</param>
    /// <param name="second">The second token map whose values take precedence.</param>
    /// <returns>A merged token map.</returns>
    internal static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        var tokens = new Dictionary<string, string>(first);

        foreach (var (key, value) in second)
        {
            tokens[key] = value;
        }

        return tokens;
    }
}
