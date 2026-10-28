namespace LiteBus.Testing;

/// <summary>
///     Declares the store schema identity a test expects from inbox or outbox diagnostics.
/// </summary>
/// <remarks>
///     <see cref="SchemaName" /> and <see cref="TableName" /> are optional pins: a <see langword="null" /> value skips
///     that check, while any other value requires an exact ordinal match.
/// </remarks>
public sealed record StoreSchemaExpectation
{
    /// <summary>
    ///     Gets the expected store component name, for example <c>inbox</c> or <c>outbox</c>.
    /// </summary>
    public required string Component { get; init; }

    /// <summary>
    ///     Gets the expected schema version required by the current LiteBus release.
    /// </summary>
    public required int ExpectedVersion { get; init; }

    /// <summary>
    ///     Gets the expected version recorded in store metadata.
    /// </summary>
    public required int RecordedVersion { get; init; }

    /// <summary>
    ///     Gets the expected database schema name, or <see langword="null" /> to skip the schema name check.
    /// </summary>
    public string? SchemaName { get; init; }

    /// <summary>
    ///     Gets the expected store table name, or <see langword="null" /> to skip the table name check.
    /// </summary>
    public string? TableName { get; init; }
}
