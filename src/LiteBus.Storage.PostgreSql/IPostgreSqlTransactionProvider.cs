using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Supplies the active PostgreSQL connection and transaction for ambient transactional messaging writes.
/// </summary>
/// <remarks>
///     Applications register a scoped implementation and activate it at unit-of-work start (for example middleware or a
///     scoped service that opens <c>BeginTransactionAsync</c> and enlists other libraries such as Marten on the same
///     transaction). LiteBus inbox and outbox participants read the active pair when
///     <c>EnableAmbientTransactionProvider()</c> is configured on PostgreSQL storage modules.
/// </remarks>
public interface IPostgreSqlTransactionProvider
{
    /// <summary>
    ///     Attempts to return the active PostgreSQL connection and transaction for the current scope.
    /// </summary>
    /// <param name="connection">When this method returns <see langword="true" />, the open connection participating in the unit of work.</param>
    /// <param name="transaction">When this method returns <see langword="true" />, the transaction that should contain messaging writes.</param>
    /// <returns>
    ///     <see langword="true" /> when an active connection and transaction are available; otherwise, <see langword="false" />.
    /// </returns>
    bool TryGetCurrent(
        [NotNullWhen(true)] out NpgsqlConnection? connection,
        [NotNullWhen(true)] out NpgsqlTransaction? transaction);
}
