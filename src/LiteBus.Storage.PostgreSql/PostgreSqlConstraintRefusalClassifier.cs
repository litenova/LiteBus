using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Turns a PostgreSQL unique or exclusion violation on a mapped constraint into a denial refusal.
/// </summary>
/// <remarks>
///     <para>
///         A duplicate key or a conflicting exclusion range raised by a unit-of-work commit never passed a guard, so
///         without a classifier it reaches the caller as a <see cref="PostgresException" /> every application maps by
///         hand at its boundary. Registered through <c>MessageModuleBuilder.UseFaultRefusalClassifierInstance</c>, this
///         classifier ends the mediation as <see cref="Refusal" /> denial carrying the mapped code instead, so a
///         <c>TrySendAsync</c> call reports it as a value.
///     </para>
///     <para>
///         The violation is a denial rather than invalid input: the message was well-formed, but the state change it
///         asked for conflicts with committed state. Only violations whose <see cref="PostgresException.ConstraintName" />
///         is present in the configured map are recognized; every other fault passes through untouched.
///     </para>
/// </remarks>
public sealed class PostgreSqlConstraintRefusalClassifier : IFaultRefusalClassifier
{
    /// <summary>
    ///     The SQLSTATE code PostgreSQL reports for a unique violation.
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    ///     The SQLSTATE code PostgreSQL reports for an exclusion violation.
    /// </summary>
    private const string ExclusionViolationSqlState = "23P01";

    /// <summary>
    ///     The constraint names this classifier recognizes, each mapped to the refusal code it reports.
    /// </summary>
    private readonly Dictionary<string, string> _constraintCodes;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlConstraintRefusalClassifier" /> class.
    /// </summary>
    /// <param name="constraintCodes">
    ///     The constraint names to recognize, each mapped to the refusal code reported for it. Compared with
    ///     ordinal case sensitivity, because PostgreSQL constraint names are case sensitive.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="constraintCodes" /> is <see langword="null" />.</exception>
    public PostgreSqlConstraintRefusalClassifier(IReadOnlyDictionary<string, string> constraintCodes)
    {
        ArgumentNullException.ThrowIfNull(constraintCodes);

        _constraintCodes = new Dictionary<string, string>(constraintCodes, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlConstraintRefusalClassifier" /> class.
    /// </summary>
    /// <param name="options">The options carrying the constraint names to recognize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    public PostgreSqlConstraintRefusalClassifier(Options options)
        : this(ResolveConstraintCodes(options))
    {
    }

    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(exception);

        refusal = default;

        // A commit fault usually arrives wrapped: the store, the session, or the unit of work adds its own layer
        // around the PostgresException, so the whole chain is walked rather than just the top exception.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState is UniqueViolationSqlState or ExclusionViolationSqlState
                && postgres.ConstraintName is { Length: > 0 } constraintName
                && _constraintCodes.TryGetValue(constraintName, out var code))
            {
                refusal = Refusal.Denied(
                    $"The operation conflicts with existing data (constraint '{constraintName}').",
                    code);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Reads the constraint map out of the supplied options.
    /// </summary>
    /// <param name="options">The options carrying the constraint names to recognize.</param>
    /// <returns>The constraint map the dictionary constructor snapshots.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    private static IReadOnlyDictionary<string, string> ResolveConstraintCodes(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ConstraintCodes;
    }

    /// <summary>
    ///     The constraint names a <see cref="PostgreSqlConstraintRefusalClassifier" /> recognizes.
    /// </summary>
    /// <param name="ConstraintCodes">
    ///     The constraint names to recognize, each mapped to the refusal code reported for it. Compared with
    ///     ordinal case sensitivity, because PostgreSQL constraint names are case sensitive.
    /// </param>
    public sealed record Options(IReadOnlyDictionary<string, string> ConstraintCodes);
}
