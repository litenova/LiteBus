using System;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     The Marten document that carries one idempotency key for <see cref="MartenIdempotencyStore" />.
/// </summary>
/// <remarks>
///     <para>
///         The document identity is the scoped idempotency key, so two concurrent claims of one key contend on one
///         row and only one of them is granted. Marten creates the storage for this document on first use under its
///         default auto-create setting; the application only names it explicitly when it wants to pin the schema,
///         with <c>options.Schema.For&lt;MartenIdempotencyRecord&gt;()</c> in its Marten configuration.
///     </para>
///     <para>
///         The properties are settable rather than init-only because Marten materializes documents through their
///         setters, the same documented exception the entity types of the relational adapters rely on.
///     </para>
/// </remarks>
public sealed class MartenIdempotencyRecord
{
    /// <summary>
    ///     Gets or sets the scoped idempotency key, which is the document identity.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the fingerprint the claimant supplied, or <see langword="null" /> when it supplied none.
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the key was applied by a completed mediation.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    ///     Gets or sets the serialized result recorded when the key was applied, when the declaration replays it.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    ///     Gets or sets the token fencing the current claim, or <see langword="null" /> for a document written before
    ///     fencing existed, which no settle matches.
    /// </summary>
    public string? ClaimToken { get; set; }

    /// <summary>
    ///     Gets or sets when the current claim was granted, read from the store clock.
    /// </summary>
    public DateTimeOffset ClaimedAt { get; set; }

    /// <summary>
    ///     Gets or sets when the key was applied, or <see langword="null" /> while the claim is unsettled.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
