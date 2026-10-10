using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One row of an audit catalogue: an audited use case, what it acts on, and how it is grouped.
/// </summary>
/// <param name="Action">The use-case identity written to every record for this message.</param>
/// <param name="MessageType">The message that produces the record.</param>
/// <param name="Category">The category the record is grouped under for review and retention.</param>
/// <param name="TargetKind">The kind of resource the message acts on.</param>
/// <param name="ReasonRequired">Whether the handler must supply a justification.</param>
/// <remarks>
///     <para>
///         A catalogue of audited actions is a compliance artifact many teams maintain by hand and keep wrong, and it
///         is a pure function of the declarations. These rows are that function's output.
///     </para>
///     <para>
///         Rows rather than a rendered document, deliberately. What a compliance process consumes differs per team: a
///         wiki page, a spreadsheet attached to an audit, a row in a control register. A library that emitted only
///         Markdown would serve one of those and get in the way of the rest.
///     </para>
///     <para>
///         It carries only what LiteBus declares. An application's own declarations, such as the permission a command
///         requires, are read from <see cref="MessageCatalogEntry.Metadata" /> by whatever projects the rest of the
///         matrix, because only the application knows what those value types mean.
///     </para>
/// </remarks>
public sealed record AuditCatalogueRow(
    string Action,
    Type MessageType,
    string? Category,
    string? TargetKind,
    bool ReasonRequired);
