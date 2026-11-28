using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Loads embedded audit SQL templates and renders identifier placeholders.
/// </summary>
/// <remarks>
///     This loader mirrors the shared PostgreSQL script loading the other LiteBus PostgreSQL stores use through
///     internals of <c>LiteBus.Storage.PostgreSql</c>. That package does not grant this package friend access yet,
///     so the audit store loads its own embedded resources directly until the grant lands and the two can converge.
///     The manifest name follows <c>{AssemblyName}.Sql.{relative.path.with.dots}.sql</c>, and the same files are
///     packed loose under <c>sql/</c> for migration copy-paste. Runtime code always reads the embedded copy so
///     behavior matches the package version.
/// </remarks>
internal static class PostgreSqlAuditSchemaSqlLoader
{
    /// <summary>
    ///     Loads one embedded SQL template and replaces <c>{{TokenName}}</c> placeholders.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the SQL file.</param>
    /// <param name="relativePath">
    ///     The path relative to the package <c>Sql</c> folder, for example <c>audit/v1/create.sql</c>.
    /// </param>
    /// <param name="tokens">Placeholder values such as <c>QualifiedRecordsTableName</c>.</param>
    /// <returns>The rendered SQL batch with trailing whitespace removed.</returns>
    /// <exception cref="DurableStorageConfigurationException">
    ///     The embedded resource name cannot be resolved.
    /// </exception>
    internal static string LoadAndRender(
        Assembly assembly,
        string relativePath,
        IReadOnlyDictionary<string, string> tokens)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(tokens);

        var template = Load(assembly, relativePath);
        var rendered = template;

        foreach (var (token, value) in tokens)
        {
            rendered = rendered.Replace($"{{{{{token}}}}}", value, StringComparison.Ordinal);
        }

        return rendered.TrimEnd();
    }

    /// <summary>
    ///     Loads one embedded SQL template without token replacement.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the SQL file.</param>
    /// <param name="relativePath">The path relative to the package <c>Sql</c> folder.</param>
    /// <returns>The raw SQL template text.</returns>
    /// <exception cref="DurableStorageConfigurationException">
    ///     The embedded resource name cannot be resolved.
    /// </exception>
    internal static string Load(Assembly assembly, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedPath = relativePath.Replace('/', '.').Replace('\\', '.');
        var resourceName = $"{assembly.GetName().Name}.Sql.{normalizedPath}";

        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new DurableStorageConfigurationException(
                $"Embedded SQL resource '{resourceName}' was not found. " +
                "Verify the file exists under Sql/ and is included as an EmbeddedResource in the project file.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
