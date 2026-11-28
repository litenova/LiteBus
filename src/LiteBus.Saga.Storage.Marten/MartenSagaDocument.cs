using System;
using Marten.Schema;

namespace LiteBus.Saga.Storage.Marten;

/// <summary>
///     The Marten document that carries one saga instance for <see cref="MartenSagaStore" />.
/// </summary>
/// <remarks>
///     <para>
///         The document identity is the escaped composite of the normalized tenant identifier, the saga definition
///         identifier, and the correlation identifier, so the tenant participates in the key exactly as it does in
///         the relational saga stores and two tenants never share a row. The parts are also stored as scalar
///         properties so queries filter on them without unpacking the identity.
///     </para>
///     <para>
///         Marten creates the storage for this document on first use under its default auto-create setting; the
///         application only names it explicitly when it wants to pin the schema, with
///         <c>options.Schema.For&lt;MartenSagaDocument&gt;()</c> in its Marten configuration.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Marten materializes documents through their
///         setters, the same documented exception the entity types of the relational adapters rely on.
///     </para>
/// </remarks>
[UseOptimisticConcurrency]
public sealed class MartenSagaDocument
{
    /// <summary>
    ///     Gets or sets the escaped composite identity built from tenant, saga definition, and correlation.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the correlation identifier that groups related saga messages.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the saga definition identifier used to resolve state shape and storage partition.
    /// </summary>
    public string SagaDefinitionId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the normalized tenant identifier, or an empty string for an unscoped saga row.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the serialized saga state JSON.
    /// </summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the optimistic lock version observed on load and required on the next save.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the saga instance is completed.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    ///     Gets or sets the durable inbox message identifier most recently applied to this saga row.
    /// </summary>
    public Guid? LastAppliedMessageId { get; set; }

    /// <summary>
    ///     Gets or sets the UTC timestamp when the row was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets the UTC timestamp when the row was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
