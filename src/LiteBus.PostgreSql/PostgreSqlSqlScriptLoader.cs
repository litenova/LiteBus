using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LiteBus.PostgreSql;

/// <summary>
///     Loads embedded SQL templates and renders identifier placeholders.
/// </summary>
/// <remarks>
///     <para>
///         SQL files under each package's <c>Sql/</c> folder are included as embedded resources at
///         build time. The manifest name follows
///         <c>{AssemblyName}.Sql.{relative.path.with.dots}.sql</c>, for example
///         <c>LiteBus.Inbox.PostgreSql.Sql.inbox.v1.create.sql</c>.
///     </para>
///     <para>
///         The same files are also packed loose into the NuGet package under <c>sql/</c> for migration copy-paste. Runtime
///         code always reads the embedded copy so behavior matches the package version even when loose files are not
///         deployed with the application.
///     </para>
/// </remarks>
internal static class PostgreSqlSqlScriptLoader
{
    /// <summary>
    ///     Loads one embedded SQL template and replaces <c>{{TokenName}}</c> placeholders.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the SQL file.</param>
    /// <param name="relativePath">
    ///     The path relative to the package <c>Sql</c> folder without extension segments duplicated in the
    ///     assembly name, for example <c>inbox/v1/create.sql</c>.
    /// </param>
    /// <param name="tokens">Placeholder values such as <c>QualifiedTableName</c>.</param>
    /// <returns>The rendered SQL batch.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the embedded resource name cannot be resolved.</exception>
    internal static string LoadAndRender(Assembly assembly, string relativePath, IReadOnlyDictionary<string, string> tokens)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(tokens);

        var template = Load(assembly, relativePath);
        return PostgreSqlSchemaScriptRenderer.Render(template, tokens);
    }

    /// <summary>
    ///     Loads one embedded SQL template without token replacement.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the SQL file.</param>
    /// <param name="relativePath">The path relative to the package <c>Sql</c> folder.</param>
    /// <returns>The raw SQL template text.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the embedded resource name cannot be resolved.</exception>
    internal static string Load(Assembly assembly, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedPath = relativePath.Replace('/', '.').Replace('\\', '.');
        var resourceName = $"{assembly.GetName().Name}.Sql.{normalizedPath}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource '{resourceName}' was not found. " +
                $"Verify the file exists under Sql/ and is included as an EmbeddedResource in the project file.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
