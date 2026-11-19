using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     The Entity Framework Core document that carries one audit record for <see cref="EfCoreAuditTrail{TDocument}" />.
/// </summary>
/// <remarks>
///     <para>
///         A flattened copy of <see cref="AuditRecord" />: one document per mediated message, with the actor unfolded
///         into scalar columns so a review can query who acted without unpacking a nested object. Applications own
///         migrations; apply <see cref="AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> in
///         <c>OnModelCreating</c> or call the same fluent configuration from a design-time factory.
///     </para>
///     <para>
///         The scalar properties are settable rather than init-only because Entity Framework Core materializes
///         entities through their setters, the same documented exception the entity types of the relational adapters
///         rely on.
///     </para>
///     <para>
///         When the audit hash chain is enabled, every document also carries its <see cref="Segment" />, its
///         <see cref="Sequence" /> number inside the segment, the <see cref="PreviousHash" /> of its predecessor,
///         its own <see cref="Hash" />, and the <see cref="HashScheme" /> the hash was computed with. Documents
///         written without the chain leave the segment and the hashes empty and the sequence at zero.
///     </para>
/// </remarks>
public sealed class EfCoreAuditRecord : IAuditChainDocument
{
    /// <summary>
    ///     Gets or sets the document identity, which is the identity of the audit record it was mapped from.
    /// </summary>
    /// <remarks>
    ///     Keying the document by the record identity is what lets a redelivered record overwrite rather than
    ///     duplicate: the store holds one document per record id.
    /// </remarks>
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
    ///     Gets or sets the additional non-identifying properties attached by the handler, serialized as a JSON
    ///     object with keys in ordinal order.
    /// </summary>
    public string PropertiesJson { get; set; } = "{}";

    /// <summary>
    ///     Gets or sets the additional non-identifying properties attached by the handler.
    /// </summary>
    /// <value>The properties as a dictionary.</value>
    /// <remarks>
    ///     Each read deserializes <see cref="PropertiesJson" /> again, so mutating the returned dictionary does
    ///     not mark the entity modified. Assign a new dictionary to persist changed properties.
    /// </remarks>
    [NotMapped]
    public Dictionary<string, string> Properties
    {
        get => AuditStoreJson.DeserializeProperties(PropertiesJson);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            PropertiesJson = AuditStoreJson.SerializeProperties(value);
        }
    }

    /// <summary>
    ///     Gets or sets the hash-chain segment the document was appended to, or <see langword="null" /> when the
    ///     document was written without the hash chain.
    /// </summary>
    public string? Segment { get; set; }

    /// <summary>
    ///     Gets or sets the sequence number of the document inside its segment, starting at one, or zero when
    ///     the document was written without the hash chain or is staged but not linked yet.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    ///     Gets or sets the chain hash of the previous document in the segment, or <see langword="null" /> for
    ///     the first document of a segment and for documents written without the hash chain.
    /// </summary>
    public string? PreviousHash { get; set; }

    /// <summary>
    ///     Gets or sets the chain hash over the canonical form of the document, or <see langword="null" /> when
    ///     the document was written without the hash chain or is staged but not linked yet.
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>
    ///     Gets or sets the name of the hash scheme the hash was computed with, or <see langword="null" /> for
    ///     documents written without the hash chain and for staged documents that are not linked yet.
    /// </summary>
    /// <value>
    ///     The scheme name, which verification uses to pick the canonical form, link rule, and hasher per
    ///     document.
    /// </value>
    public string? HashScheme { get; set; }

    /// <summary>
    ///     Gets or sets when the document was staged for linking.
    /// </summary>
    /// <remarks>
    ///     The store stamps this when the document is staged. The linker reads staged documents oldest first,
    ///     and the sweep reads only documents staged longer ago than its age.
    /// </remarks>
    public DateTimeOffset StagedAt { get; set; }
}
