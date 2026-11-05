using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Applies provider-specific mappings for UTC durable timestamps.
/// </summary>
/// <remarks>
///     SQLite stores sortable UTC ticks. MySQL stores <c>datetime(6)</c>, which both MySQL providers write in UTC. Pomelo
///     maps <see cref="DateTimeOffset" /> to <c>datetime(6)</c> by default, but Oracle's <c>MySql.EntityFrameworkCore</c>
///     maps it to <c>datetime</c>, which rounds every timestamp to a whole second and so moves lease expiry and
///     visibility times.
/// </remarks>
internal static class EfCoreDateTimeOffsetModelExtensions
{
    /// <summary>
    ///     The MySQL column type for durable timestamps, with microsecond precision.
    /// </summary>
    private const string MySqlTimestampColumnType = "datetime(6)";

    /// <summary>
    ///     Maps a required timestamp to sortable UTC ticks on SQLite and to <c>datetime(6)</c> on MySQL.
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
        else if (provider == EfCoreStorageProvider.MySql)
        {
            property.HasColumnType(MySqlTimestampColumnType);
        }

        return property;
    }

    /// <summary>
    ///     Maps an optional timestamp to sortable UTC ticks on SQLite and to <c>datetime(6)</c> on MySQL.
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
        else if (provider == EfCoreStorageProvider.MySql)
        {
            property.HasColumnType(MySqlTimestampColumnType);
        }

        return property;
    }
}
