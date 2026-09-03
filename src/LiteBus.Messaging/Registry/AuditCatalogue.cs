using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Projects the audited messages of a catalog into rows, and those rows into a document.
/// </summary>
/// <remarks>
///     <para>
///         The audit catalogue and the authorization matrix are the two artifacts a permissioned application tends to
///         maintain by hand as a side effect of a migration, and both are pure functions of the declarations. This
///         builds the half LiteBus can build: the actions, their categories, and what they act on.
///     </para>
///     <para>
///         The other half is the application's. A required permission is an application value type, so project it
///         from <see cref="MessageCatalogEntry.Metadata" /> alongside these rows; only the application knows what its
///         own declarations mean.
///     </para>
/// </remarks>
public static class AuditCatalogue
{
    /// <summary>
    ///     Projects every audited message into a row.
    /// </summary>
    /// <param name="catalog">The catalog to read.</param>
    /// <returns>One row per audited message, ordered by action so two runs produce the same document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     An exempt message is absent, because a catalogue of audited actions is what this produces. Enumerate the
    ///     catalog itself and read <see cref="DeclarationExemptions" /> to report the exemptions and their rationales,
    ///     which is a different artifact answering a different question.
    /// </remarks>
    public static IReadOnlyList<AuditCatalogueRow> ToRows(this IMessageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.Audited()
            .Select(static entry => new AuditCatalogueRow(
                entry.Audit!.Action,
                entry.MessageType,
                entry.Audit.Category,
                entry.Audit.TargetKind,
                entry.Audit.ReasonRequired))
            .OrderBy(static row => row.Action, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Renders the audited messages as a Markdown table.
    /// </summary>
    /// <param name="catalog">The catalog to read.</param>
    /// <returns>The rendered table, or a single line saying nothing is audited.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     One formatter over <see cref="ToRows" />, for the common case of pasting the catalogue into a wiki or
    ///     committing it beside the code. Write your own over the rows for anything else; that is why the rows are
    ///     the primary surface.
    /// </remarks>
    public static string ToMarkdown(this IMessageCatalog catalog)
    {
        var rows = catalog.ToRows();

        if (rows.Count == 0)
        {
            return "No message declares an audited position.";
        }

        var document = new StringBuilder();
        document.AppendLine("| Action | Category | Target | Reason required | Message |");
        document.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var row in rows)
        {
            document.Append("| ").Append(row.Action)
                    .Append(" | ").Append(row.Category ?? "-")
                    .Append(" | ").Append(row.TargetKind ?? "-")
                    .Append(" | ").Append(row.ReasonRequired ? "yes" : "no")
                    .Append(" | ").Append(row.MessageType.Name)
                    .AppendLine(" |");
        }

        document.Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" audited action");

        if (rows.Count != 1)
        {
            document.Append('s');
        }

        return document.Append('.').ToString();
    }
}
