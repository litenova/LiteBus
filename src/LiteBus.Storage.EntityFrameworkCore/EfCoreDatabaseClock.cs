using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Reads the authoritative UTC time from a relational provider connection.
/// </summary>
internal static class EfCoreDatabaseClock
{
    /// <summary>
    ///     Reads the current UTC timestamp through the active database connection and transaction.
    /// </summary>
    /// <param name="dbContext">The database context that owns the connection.</param>
    /// <param name="provider">The resolved relational provider.</param>
    /// <param name="cancellationToken">A token that cancels the scalar query.</param>
    /// <returns>The provider's current timestamp normalized to UTC.</returns>
    internal static async Task<DateTimeOffset> GetUtcNowAsync(
        DbContext dbContext,
        EfCoreStorageProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;

        if (shouldClose)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = GetCurrentTimestampSql(provider);
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();

            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return ConvertToUtc(value, provider);
        }
        finally
        {
            if (shouldClose)
            {
                await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Returns the provider-specific scalar query for the current UTC timestamp.
    /// </summary>
    /// <param name="provider">The resolved storage provider.</param>
    /// <returns>The scalar SQL statement.</returns>
    private static string GetCurrentTimestampSql(EfCoreStorageProvider provider)
    {
        return provider switch
        {
            EfCoreStorageProvider.PostgreSql => "SELECT CURRENT_TIMESTAMP",
            EfCoreStorageProvider.SqlServer => "SELECT SYSUTCDATETIME()",
            EfCoreStorageProvider.MySql => "SELECT UTC_TIMESTAMP(6)",
            EfCoreStorageProvider.Sqlite => "SELECT strftime('%Y-%m-%dT%H:%M:%fZ', 'now')",
            _ => throw new NotSupportedException(
                $"Database clock access is not supported for Entity Framework provider '{provider}'.")
        };
    }

    /// <summary>
    ///     Converts a provider scalar timestamp to a UTC offset value.
    /// </summary>
    /// <param name="value">The scalar value returned by the provider.</param>
    /// <param name="provider">The provider used to improve conversion errors.</param>
    /// <returns>The timestamp normalized to UTC.</returns>
    private static DateTimeOffset ConvertToUtc(object? value, EfCoreStorageProvider provider)
    {
        if (value is DateTimeOffset offset)
        {
            return offset.ToUniversalTime();
        }

        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        if (value is string text && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Entity Framework provider '{provider}' returned an unsupported database timestamp value.");
    }
}
