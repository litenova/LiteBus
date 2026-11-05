using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace LiteBus.Storage.EntityFrameworkCore.Stores;

/// <summary>
///     Recognizes a unique-constraint violation in a failed save, whichever database provider raised it.
/// </summary>
/// <remarks>
///     <para>
///         The decision reads the structured error the driver attaches to its <see cref="DbException" />, not the
///         message text, which the server localizes and providers reword between versions. PostgreSQL reports
///         SQLSTATE <c>23505</c> through <see cref="DbException.SqlState" />. SQL Server (<c>2627</c>, <c>2601</c>)
///         and both MySQL drivers (<c>1062</c>, <c>1586</c>) report an error number on a public <c>Number</c>
///         property, and SQLite reports an extended result code (<c>2067</c>, <c>1555</c>) on
///         <c>SqliteExtendedErrorCode</c>. Those two properties are read by name, because this package references no
///         provider driver.
///     </para>
///     <para>
///         The message text decides only when no exception in the chain carries a structured error, which happens
///         with a provider LiteBus does not know.
///     </para>
/// </remarks>
internal static class EfCoreUniqueViolation
{
    /// <summary>
    ///     The SQLSTATE PostgreSQL reports for a unique violation.
    /// </summary>
    private const string PostgreSqlUniqueViolationState = "23505";

    /// <summary>
    ///     The name of the error-number property on SQL Server and MySQL driver exceptions.
    /// </summary>
    private const string ErrorNumberProperty = "Number";

    /// <summary>
    ///     The name of the extended result code property on SQLite driver exceptions.
    /// </summary>
    private const string SqliteExtendedErrorCodeProperty = "SqliteExtendedErrorCode";

    /// <summary>
    ///     Caches the integer property, or its absence, for each exception type and property name, so the reflection
    ///     lookup runs once per driver exception type for the life of the process.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> IntegerProperties = new();

    /// <summary>
    ///     Determines whether a failed save was caused by a unique-constraint violation.
    /// </summary>
    /// <param name="exception">The exception the save raised.</param>
    /// <returns>
    ///     <see langword="true" /> when a driver error in the chain reports a unique violation; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    internal static bool IsUniqueViolation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sawStructuredError = false;

        foreach (var current in EnumerateExceptions(exception))
        {
            if (current is not DbException databaseError)
            {
                continue;
            }

            switch (Classify(databaseError))
            {
                case true:
                    return true;
                case false:
                    sawStructuredError = true;
                    break;
            }
        }

        if (sawStructuredError)
        {
            return false;
        }

        foreach (var current in EnumerateExceptions(exception))
        {
            if (current.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Classifies one driver error from the structured code it carries.
    /// </summary>
    /// <param name="databaseError">The driver error.</param>
    /// <returns>
    ///     <see langword="true" /> for a unique violation, <see langword="false" /> for any other structured error, or
    ///     <see langword="null" /> when the error carries no code this method understands.
    /// </returns>
    private static bool? Classify(DbException databaseError)
    {
        if (string.Equals(databaseError.SqlState, PostgreSqlUniqueViolationState, StringComparison.Ordinal))
        {
            return true;
        }

        if (TryReadInteger(databaseError, SqliteExtendedErrorCodeProperty, out var sqliteCode))
        {
            // SQLITE_CONSTRAINT_UNIQUE and SQLITE_CONSTRAINT_PRIMARYKEY.
            return sqliteCode is 2067 or 1555;
        }

        if (TryReadInteger(databaseError, ErrorNumberProperty, out var number))
        {
            // SQL Server unique key and unique index; MySQL ER_DUP_ENTRY and ER_DUP_ENTRY_WITH_KEY_NAME.
            return number is 2627 or 2601 or 1062 or 1586;
        }

        return string.IsNullOrEmpty(databaseError.SqlState) ? null : false;
    }

    /// <summary>
    ///     Reads a public integer property from a driver exception.
    /// </summary>
    /// <param name="databaseError">The driver error.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value when the property exists.</param>
    /// <returns><see langword="true" /> when the exception type has the property; otherwise, <see langword="false" />.</returns>
    private static bool TryReadInteger(DbException databaseError, string propertyName, out int value)
    {
        var property = IntegerProperties.GetOrAdd(
            (databaseError.GetType(), propertyName),
            static key => key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance) is { } candidate
                          && candidate.PropertyType == typeof(int)
                ? candidate
                : null);

        if (property?.GetValue(databaseError) is int number)
        {
            value = number;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }
}
