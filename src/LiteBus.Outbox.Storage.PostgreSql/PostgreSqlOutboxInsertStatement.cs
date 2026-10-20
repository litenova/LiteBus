using System.Collections.Generic;
using System.Text;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Represents one parameterized outbox row insert owned by the PostgreSQL outbox schema.
/// </summary>
/// <remarks>
///     <para>
///         The statement targets <see cref="PostgreSqlOutboxSchema.CurrentSchemaVersion" /> and inserts the same
///         columns <see cref="PostgreSqlOutboxStore" /> writes, so a unit of work that queues SQL writes the exact
///         row the PostgreSQL store writes. Callers must not edit the statement text: a schema change updates
///         <see cref="PostgreSqlOutboxSchema.CreateInsertStatement(OutboxEnvelope, PostgreSqlOutboxStoreOptions?)" />
///         in one place.
///     </para>
///     <para>
///         The statement ends with <c>ON CONFLICT DO NOTHING</c> and has no <c>RETURNING</c> clause, so a conflicting
///         insert is skipped silently. Idempotency resolution is the caller job: resolve duplicates before queueing
///         rather than reading them back from a skipped insert.
///     </para>
/// </remarks>
public sealed record PostgreSqlOutboxInsertStatement
{
    /// <summary>
    ///     Gets the parameterized insert text using Npgsql positional parameters (<c>$1</c> through <c>$n</c>).
    /// </summary>
    public required string Sql { get; init; }

    /// <summary>
    ///     Gets the parameter values in placeholder order, with <see cref="System.DBNull.Value" /> for every absent
    ///     value.
    /// </summary>
    public required IReadOnlyList<object> Values { get; init; }

    /// <summary>
    ///     Returns the insert text with each positional marker replaced by <paramref name="placeholder" />.
    /// </summary>
    /// <param name="placeholder">
    ///     The single character that replaces each <c>$n</c> marker, keeping explicit casts. Use the placeholder
    ///     character of the unit of work that executes the statement, for example <c>?</c> for Marten
    ///     <c>QueueSqlCommand</c>.
    /// </param>
    /// <returns>The insert text with one <paramref name="placeholder" /> per value.</returns>
    public string FormatSql(char placeholder)
    {
        var builder = new StringBuilder(Sql.Length);
        var index = 0;

        while (index < Sql.Length)
        {
            if (Sql[index] == '$' && index + 1 < Sql.Length && char.IsDigit(Sql[index + 1]))
            {
                index += 2;

                while (index < Sql.Length && char.IsDigit(Sql[index]))
                {
                    index++;
                }

                builder.Append(placeholder);
                continue;
            }

            builder.Append(Sql[index]);
            index++;
        }

        return builder.ToString();
    }
}
