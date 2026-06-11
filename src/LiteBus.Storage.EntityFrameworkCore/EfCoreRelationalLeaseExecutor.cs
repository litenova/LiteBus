using System.ComponentModel.DataAnnotations.Schema;
using LiteBus.Storage.EntityFrameworkCore.Leasing;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Executes provider-specific inbox and outbox lease SQL through an Entity Framework Core context.
/// </summary>
internal static class EfCoreRelationalLeaseExecutor
{
    /// <summary>
    ///     Leases rows using PostgreSQL skip-locked semantics.
    /// </summary>
    /// <typeparam name="TRow">The mapped lease row type.</typeparam>
    /// <param name="dbContext">The database context.</param>
    /// <param name="component">The lease component.</param>
    /// <param name="provider">The storage provider.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="pendingStatus">The pending status value.</param>
    /// <param name="failedStatus">The failed status value.</param>
    /// <param name="now">The current timestamp.</param>
    /// <param name="processingStatus">The processing status value.</param>
    /// <param name="batchSize">The lease batch size.</param>
    /// <param name="leaseOwner">The lease owner.</param>
    /// <param name="leaseExpiresAt">The lease expiration timestamp.</param>
    /// <param name="tenantId">The optional tenant filter applied to candidate rows.</param>
    /// <param name="staleCutoff">The earliest created timestamp eligible for stale in-flight reclaim.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased rows.</returns>
    internal static Task<List<TRow>> LeasePostgreSqlAsync<TRow>(
        DbContext dbContext,
        EfCoreLeaseComponent component,
        EfCoreStorageProvider provider,
        string schemaName,
        string tableName,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int processingStatus,
        int batchSize,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        string? tenantId,
        DateTimeOffset staleCutoff,
        CancellationToken cancellationToken)
        where TRow : class
    {
        var qualifiedTableName = EfCoreRelationalTableQualifier.Qualify(provider, schemaName, tableName);
        var sql = EfCorePostgreSqlLeaseSql.Build(component, qualifiedTableName);

        return dbContext.Database
            .SqlQueryRaw<TRow>(
                sql,
                pendingStatus,
                failedStatus,
                now,
                processingStatus,
                batchSize,
                leaseOwner,
                leaseExpiresAt,
                (object?) tenantId ?? DBNull.Value,
                staleCutoff)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Leases rows using SQL Server read-past update semantics.
    /// </summary>
    /// <typeparam name="TRow">The mapped lease row type.</typeparam>
    /// <param name="dbContext">The database context.</param>
    /// <param name="component">The lease component.</param>
    /// <param name="provider">The storage provider.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="pendingStatus">The pending status value.</param>
    /// <param name="failedStatus">The failed status value.</param>
    /// <param name="now">The current timestamp.</param>
    /// <param name="processingStatus">The processing status value.</param>
    /// <param name="batchSize">The lease batch size.</param>
    /// <param name="leaseOwner">The lease owner.</param>
    /// <param name="leaseExpiresAt">The lease expiration timestamp.</param>
    /// <param name="tenantId">The optional tenant filter applied to candidate rows.</param>
    /// <param name="staleCutoff">The earliest created timestamp eligible for stale in-flight reclaim.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased rows.</returns>
    internal static Task<List<TRow>> LeaseSqlServerAsync<TRow>(
        DbContext dbContext,
        EfCoreLeaseComponent component,
        EfCoreStorageProvider provider,
        string schemaName,
        string tableName,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int processingStatus,
        int batchSize,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        string? tenantId,
        DateTimeOffset staleCutoff,
        CancellationToken cancellationToken)
        where TRow : class
    {
        var qualifiedTableName = EfCoreRelationalTableQualifier.Qualify(provider, schemaName, tableName);
        var sql = EfCoreSqlServerLeaseSql.Build(component, qualifiedTableName);

        return dbContext.Database
            .SqlQueryRaw<TRow>(
                sql,
                pendingStatus,
                failedStatus,
                now,
                processingStatus,
                batchSize,
                leaseOwner,
                leaseExpiresAt,
                (object?) tenantId ?? DBNull.Value,
                staleCutoff)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Leases rows using MySQL skip-locked semantics inside one database transaction.
    /// </summary>
    /// <typeparam name="TRow">The mapped lease row type.</typeparam>
    /// <param name="dbContext">The database context.</param>
    /// <param name="component">The lease component.</param>
    /// <param name="provider">The storage provider.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="pendingStatus">The pending status value.</param>
    /// <param name="failedStatus">The failed status value.</param>
    /// <param name="now">The current timestamp.</param>
    /// <param name="processingStatus">The processing status value.</param>
    /// <param name="batchSize">The lease batch size.</param>
    /// <param name="leaseOwner">The lease owner.</param>
    /// <param name="leaseExpiresAt">The lease expiration timestamp.</param>
    /// <param name="tenantId">The optional tenant filter applied to candidate rows.</param>
    /// <param name="staleCutoff">The earliest created timestamp eligible for stale in-flight reclaim.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The leased rows.</returns>
    internal static async Task<List<TRow>> LeaseMySqlAsync<TRow>(
        DbContext dbContext,
        EfCoreLeaseComponent component,
        EfCoreStorageProvider provider,
        string schemaName,
        string tableName,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int processingStatus,
        int batchSize,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        string? tenantId,
        DateTimeOffset staleCutoff,
        CancellationToken cancellationToken)
        where TRow : class
    {
        var qualifiedTableName = EfCoreRelationalTableQualifier.Qualify(provider, schemaName, tableName);
        var selectSql = EfCoreMySqlLeaseSql.BuildSelectCandidates(component, qualifiedTableName);
        var updateSqlTemplate = EfCoreMySqlLeaseSql.BuildUpdate(component, qualifiedTableName);
        var reloadSqlTemplate = EfCoreMySqlLeaseSql.BuildReload(component, qualifiedTableName);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var candidateRows = await dbContext.Database
                .SqlQueryRaw<MySqlLeaseCandidateRow>(
                    selectSql,
                    pendingStatus,
                    failedStatus,
                    now,
                    processingStatus,
                    batchSize,
                    (object?) tenantId ?? DBNull.Value,
                    staleCutoff)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (candidateRows.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return [];
            }

            var candidateIds = candidateRows.ConvertAll(row => row.Value);
            var updateInClause = BuildInClause(candidateIds.Count, 9);
            var updateSql = updateSqlTemplate.Replace(EfCoreMySqlLeaseSql.InClauseToken, updateInClause, StringComparison.Ordinal);

            var updateParameters = BuildLeaseParameters(
                pendingStatus,
                failedStatus,
                now,
                processingStatus,
                batchSize,
                leaseOwner,
                leaseExpiresAt,
                tenantId,
                staleCutoff,
                candidateIds);

            await dbContext.Database.ExecuteSqlRawAsync(updateSql, updateParameters, cancellationToken)
                .ConfigureAwait(false);

            var reloadInClause = BuildInClause(candidateIds.Count, 0);
            var reloadSql = reloadSqlTemplate.Replace(EfCoreMySqlLeaseSql.InClauseToken, reloadInClause, StringComparison.Ordinal);
            var reloadParameters = BuildInParameters(candidateIds);

            var rows = await dbContext.Database
                .SqlQueryRaw<TRow>(reloadSql, reloadParameters)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return rows;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Builds a parameterized IN clause placeholder list.
    /// </summary>
    /// <param name="count">The number of placeholders.</param>
    /// <param name="startingIndex">The first parameter index.</param>
    /// <returns>The IN clause body.</returns>
    private static string BuildInClause(int count, int startingIndex)
    {
        var placeholders = new string[count];

        for (var index = 0; index < count; index++)
        {
            placeholders[index] = $"{{{startingIndex + index}}}";
        }

        return string.Join(", ", placeholders);
    }

    /// <summary>
    ///     Builds the parameter array for a MySQL lease update statement.
    /// </summary>
    /// <param name="pendingStatus">The pending status value.</param>
    /// <param name="failedStatus">The failed status value.</param>
    /// <param name="now">The current timestamp.</param>
    /// <param name="processingStatus">The processing status value.</param>
    /// <param name="batchSize">The lease batch size.</param>
    /// <param name="leaseOwner">The lease owner.</param>
    /// <param name="leaseExpiresAt">The lease expiration timestamp.</param>
    /// <param name="tenantId">The optional tenant filter applied to candidate rows.</param>
    /// <param name="staleCutoff">The earliest created timestamp eligible for stale in-flight reclaim.</param>
    /// <param name="candidateIds">The candidate identifiers.</param>
    /// <returns>The parameter array.</returns>
    private static object[] BuildLeaseParameters(
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int processingStatus,
        int batchSize,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        string? tenantId,
        DateTimeOffset staleCutoff,
        IReadOnlyList<Guid> candidateIds)
    {
        var parameters = new object[9 + candidateIds.Count];
        parameters[0] = pendingStatus;
        parameters[1] = failedStatus;
        parameters[2] = now;
        parameters[3] = processingStatus;
        parameters[4] = batchSize;
        parameters[5] = leaseOwner;
        parameters[6] = leaseExpiresAt;
        parameters[7] = (object?) tenantId ?? DBNull.Value;
        parameters[8] = staleCutoff;

        for (var index = 0; index < candidateIds.Count; index++)
        {
            parameters[9 + index] = candidateIds[index];
        }

        return parameters;
    }

    /// <summary>
    ///     Builds a parameter array containing only identifier values.
    /// </summary>
    /// <param name="candidateIds">The candidate identifiers.</param>
    /// <returns>The parameter array.</returns>
    private static object[] BuildInParameters(IReadOnlyList<Guid> candidateIds)
    {
        var parameters = new object[candidateIds.Count];

        for (var index = 0; index < candidateIds.Count; index++)
        {
            parameters[index] = candidateIds[index];
        }

        return parameters;
    }

    /// <summary>
    ///     Represents one candidate identifier selected during a MySQL lease operation.
    /// </summary>
    private sealed class MySqlLeaseCandidateRow
    {
        /// <summary>
        ///     Gets or sets the candidate identifier value.
        /// </summary>
        [Column("Value")]
        public Guid Value { get; set; }
    }
}