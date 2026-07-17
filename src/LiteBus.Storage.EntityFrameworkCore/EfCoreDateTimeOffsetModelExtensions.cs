using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Applies provider-specific mappings for UTC durable timestamps.
/// </summary>
internal static class EfCoreDateTimeOffsetModelExtensions
{
    /// <summary>
    ///     Maps a required timestamp to sortable UTC ticks when SQLite is selected.
    /// </summary>
    /// <param name="property">The timestamp property builder.</param>
    /// <param name="provider">The configured storage provider.</param>
    /// <returns>The same property builder for chaining.</returns>
    internal static PropertyBuilder<DateTimeOffset> ConfigureUtcTimestampColumn(
        this PropertyBuilder<DateTimeOffset> property,
        EfCoreStorageProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (provider == EfCoreStorageProvider.Sqlite)
        {
            property.HasConversion<long>(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
        }

        return property;
    }

    /// <summary>
    ///     Maps an optional timestamp to sortable UTC ticks when SQLite is selected.
    /// </summary>
    /// <param name="property">The timestamp property builder.</param>
    /// <param name="provider">The configured storage provider.</param>
    /// <returns>The same property builder for chaining.</returns>
    internal static PropertyBuilder<DateTimeOffset?> ConfigureUtcTimestampColumn(
        this PropertyBuilder<DateTimeOffset?> property,
        EfCoreStorageProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (provider == EfCoreStorageProvider.Sqlite)
        {
            property.HasConversion<long?>(
                value => value.HasValue ? value.Value.UtcTicks : null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        }

        return property;
    }
}
