using System.Collections.Generic;

namespace LiteBus.PostgreSql;

/// <summary>
///     Builds placeholder token maps for embedded SQL templates.
/// </summary>
internal static class PostgreSqlSchemaSqlTokens
{
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

    internal static Dictionary<string, string> ForStoreTable(IPostgreSqlStoreTableOptions options)
    {
        return new Dictionary<string, string>
        {
            ["QuotedSchemaName"] = PostgreSqlIdentifier.Quote(options.SchemaName),
            ["QualifiedTableName"] = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)
        };
    }

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
