using System;
using System.Collections.Generic;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Replaces <c>{{TokenName}}</c> placeholders in SQL templates.
/// </summary>
internal static class PostgreSqlSchemaScriptRenderer
{
    /// <summary>
    ///     Replaces every <c>{{TokenName}}</c> placeholder in a SQL template.
    /// </summary>
    /// <param name="template">The SQL template text.</param>
    /// <param name="tokens">The placeholder values keyed by token name without braces.</param>
    /// <returns>The rendered SQL batch with trailing whitespace removed.</returns>
    internal static string Render(string template, IReadOnlyDictionary<string, string> tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(tokens);

        var rendered = template;

        foreach (var (token, value) in tokens)
        {
            rendered = rendered.Replace($"{{{{{token}}}}}", value, StringComparison.Ordinal);
        }

        return rendered.TrimEnd();
    }
}
