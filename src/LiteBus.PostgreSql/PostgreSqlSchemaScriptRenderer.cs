using System;
using System.Collections.Generic;

namespace LiteBus.PostgreSql;

/// <summary>
///     Replaces <c>{{TokenName}}</c> placeholders in SQL templates.
/// </summary>
internal static class PostgreSqlSchemaScriptRenderer
{
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
