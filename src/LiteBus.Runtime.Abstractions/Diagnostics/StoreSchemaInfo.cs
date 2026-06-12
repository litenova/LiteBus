using System;
using System.Diagnostics;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Describes the schema version expected and recorded for a durable inbox or outbox store.
/// </summary>
/// <param name="Component">The store component name, for example <c>inbox</c> or <c>outbox</c>.</param>
/// <param name="ExpectedVersion">The schema version required by the current LiteBus release.</param>
/// <param name="RecordedVersion">
///     The version recorded in store metadata, or the logical version for non-relational backends.
/// </param>
/// <param name="SchemaName">The database schema name when applicable; otherwise <see langword="null" />.</param>
/// <param name="TableName">The store table name when applicable; otherwise <see langword="null" />.</param>
[DebuggerDisplay("{Component} v{RecordedVersion}/{ExpectedVersion}")]
public sealed record StoreSchemaInfo(
    string Component,
    int ExpectedVersion,
    int RecordedVersion,
    string? SchemaName = null,
    string? TableName = null)
{
    /// <summary>
    ///     Creates schema info for logical stores that do not persist version metadata.
    /// </summary>
    /// <param name="component">The store component name.</param>
    /// <param name="version">The logical schema version.</param>
    /// <returns>Schema info for in-memory and Entity Framework backends.</returns>
    public static StoreSchemaInfo ForLogicalStore(string component, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);

        return new StoreSchemaInfo(component, version, version);
    }
}
