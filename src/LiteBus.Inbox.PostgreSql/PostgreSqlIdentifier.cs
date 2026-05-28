using System;
using System.Text;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Builds quoted PostgreSQL identifiers used by the inbox store and schema creator.
/// </summary>
/// <remarks>
///     PostgreSQL does not allow parameterized schema, table, or index names. This helper centralizes quoting and index
///     name trimming so SQL text can be built without spreading identifier rules through store code.
/// </remarks>
internal static class PostgreSqlIdentifier
{
    /// <summary>
    ///     Combines a schema and table name into a quoted qualified table identifier.
    /// </summary>
    /// <param name="schemaName">The unquoted schema name supplied by store options.</param>
    /// <param name="tableName">The unquoted table name supplied by store options.</param>
    /// <returns>A qualified identifier in the form <c>"schema"."table"</c>.</returns>
    internal static string Qualify(string schemaName, string tableName)
    {
        return $"{Quote(schemaName)}.{Quote(tableName)}";
    }

    /// <summary>
    ///     Quotes one PostgreSQL identifier and escapes embedded quote characters.
    /// </summary>
    /// <param name="identifier">The unquoted identifier.</param>
    /// <returns>The quoted identifier.</returns>
    internal static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (identifier.IndexOf('\0', StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("PostgreSQL identifiers cannot contain null characters.", nameof(identifier));
        }

        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    ///     Creates a deterministic index name that stays within PostgreSQL identifier length limits.
    /// </summary>
    /// <param name="tableName">The table name used as the index name prefix.</param>
    /// <param name="suffix">The logical suffix that describes the index role.</param>
    /// <returns>A quoted index identifier.</returns>
    internal static string IndexName(string tableName, string suffix)
    {
        var builder = new StringBuilder(tableName.Length + suffix.Length + 1);

        foreach (var character in $"{tableName}_{suffix}")
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        var name = builder.ToString();

        if (name.Length > 60)
        {
            name = name[..48] + "_" + Math.Abs(name.GetHashCode(StringComparison.Ordinal)).ToString("x");
        }

        return Quote(name);
    }
}