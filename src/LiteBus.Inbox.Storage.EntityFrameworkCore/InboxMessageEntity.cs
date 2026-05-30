using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core persistence model for one inbox command row.
/// </summary>
/// <remarks>
///     Maps one-to-one to <see cref="InboxEnvelope" /> fields. Applications own migrations; apply
///     <see cref="InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> in
///     <c>OnModelCreating</c> or call the same fluent configuration from a design-time factory.
/// </remarks>
public sealed class InboxMessageEntity
{
    /// <summary>
    ///     Gets or sets the unique persisted command identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the stable command contract name.
    /// </summary>
    public string ContractName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the command contract version.
    /// </summary>
    public int ContractVersion { get; set; }

    /// <summary>
    ///     Gets or sets the serialized command payload stored as JSON text.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the UTC timestamp when the command was accepted.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets the earliest UTC timestamp at which the command may be processed.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; set; }

    /// <summary>
    ///     Gets or sets the number of processing attempts.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    ///     Gets or sets the current processing status.
    /// </summary>
    public InboxStatus Status { get; set; }

    /// <summary>
    ///     Gets or sets the optional idempotency key.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    ///     Gets or sets the optional processing lease owner.
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    ///     Gets or sets the optional UTC timestamp when the processing lease expires.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>
    ///     Gets or sets the optional latest processing error.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    ///     Gets or sets the optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Gets or sets the optional causation identifier.
    /// </summary>
    public string? CausationId { get; set; }

    /// <summary>
    ///     Gets or sets the optional tenant identifier.
    /// </summary>
    public string? TenantId { get; set; }
}
