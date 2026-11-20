using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     The Marten document that carries one audit record for <see cref="MartenAuditTrail{TDocument}" />.
/// </summary>
/// <remarks>
///     <para>
///         A flattened copy of <see cref="AuditRecord" />: one document per mediated message, with the actor unfolded
///         into scalar columns so a review can query who acted without unpacking a nested object. Marten creates the
///         storage for this document on first use under its default auto-create setting; the application only names it
///         explicitly when it wants to pin the schema, with <c>options.Schema.For&lt;MartenAuditRecord&gt;()</c> in its
///         Marten configuration.
///     </para>
///     <para>
///         The scalar properties are settable rather than init-only because Marten materializes documents through their
///         setters, the same documented exception the entity types of the relational adapters rely on.
///         <see cref="Properties" /> stays init-only so the collection itself is never replaced after construction.
///     </para>
///     <para>
///         The document identity is the <see cref="AuditRecord.Id" /> of the record it was mapped from, so a record
///         delivered twice overwrites rather than duplicates. When the trail hash chain is enabled, every record also
///         carries its <see cref="Segment" />, its <see cref="Sequence" /> number inside the segment, the
///         <see cref="PreviousHash" /> of its predecessor, its own <see cref="Hash" />, and the
///         <see cref="HashScheme" /> the hash was computed with. A staged record that the linker has not positioned
///         yet carries sequence zero with empty hashes; records written without the chain stay that way.
///     </para>
/// </remarks>
public sealed class MartenAuditRecord : IAuditChainDocument
{
    /// <summary>
    ///     Gets or sets the document identity, which is the identifier of the audit record it was mapped from.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the use-case identity of the action, such as <c>orders.place-order</c>.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the stable identifier of the actor, when one was established.
    /// </summary>
    public string? ActorId { get; set; }

    /// <summary>
    ///     Gets or sets the kind of thing that acted, such as <c>user</c> or <c>system</c>.
    /// </summary>
    public string? ActorKind { get; set; }

    /// <summary>
    ///     Gets or sets the name recorded for a reader in place of the actor identifier.
    /// </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary>
    ///     Gets or sets the identifier of the actor this one acted for, where the action was delegated.
    /// </summary>
    public string? ActorOnBehalfOf { get; set; }

    /// <summary>
    ///     Gets or sets how the action ended.
    /// </summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>
    ///     Gets or sets the time the action occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    ///     Gets or sets how long the mediation took.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    ///     Gets or sets the category used to group the record for review and retention.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    ///     Gets or sets the kind of resource acted on, such as <c>order</c>.
    /// </summary>
    public string? TargetKind { get; set; }

    /// <summary>
    ///     Gets or sets the identifier of the resource acted on.
    /// </summary>
    public string? TargetId { get; set; }

    /// <summary>
    ///     Gets or sets the reason the action was taken, where the action requires one.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Gets or sets the stable failure code for a non-success outcome.
    /// </summary>
    public string? FailureCode { get; set; }

    /// <summary>
    ///     Gets or sets the CLR type name of the message that produced this record.
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    ///     Gets or sets the correlation identifier tying this record to the diagnostic log lines for the same request.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Gets or sets the tenant the action happened in.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Gets the additional non-identifying properties attached by the handler.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets or sets the hash-chain segment the record was appended to, or <see langword="null" /> when the record
    ///     was written without the hash chain.
    /// </summary>
    public string? Segment { get; set; }

    /// <summary>
    ///     Gets or sets the sequence number of the record inside its segment, starting at one, or zero when the
    ///     record is staged but not linked yet and when it was written without the hash chain.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    ///     Gets or sets the chain hash of the previous record in the segment, or <see langword="null" /> for the
    ///     first record of a segment, for a staged record without a position, and for records written without the
    ///     hash chain.
    /// </summary>
    public string? PreviousHash { get; set; }

    /// <summary>
    ///     Gets or sets the chain hash over the canonical form of the record, or <see langword="null" /> for a
    ///     staged record without a position and for records written without the hash chain.
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>
    ///     Gets or sets the name of the hash scheme the hash was computed with, or <see langword="null" /> for
    ///     staged records without a position, for records written without the hash chain, and for records written
    ///     before schemes existed.
    /// </summary>
    /// <value>
    ///     The scheme name, which verification uses to pick the canonical form, link rule, and hasher per record.
    ///     Records that carry <see langword="null" /> were hashed with the default scheme.
    /// </value>
    public string? HashScheme { get; set; }
}
