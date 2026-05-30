using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core persistence model for one outbox message row.
/// </summary>
/// <remarks>
///     Maps one-to-one to <see cref="OutboxEnvelope" /> fields. Applications own migrations; apply
///     <see cref="OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration" /> in
///     <c>OnModelCreating</c> or call the same fluent configuration from a design-time factory.
/// </remarks>
public sealed class OutboxMessageEntity
{
    /// <summary>
    ///     Gets or sets the unique outbox message identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the stable event contract name.
    /// </summary>
    public string ContractName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the event contract version.
    /// </summary>
    public int ContractVersion { get; set; }

    /// <summary>
    ///     Gets or sets the serialized event payload stored as JSON text.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the optional topic or channel used by external dispatchers.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    ///     Gets or sets the UTC timestamp when the event was stored.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets the earliest UTC timestamp at which the message may be published.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; set; }

    /// <summary>
    ///     Gets or sets the current publication status.
    /// </summary>
    public OutboxStatus Status { get; set; }

    /// <summary>
    ///     Gets or sets the number of publication attempts.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    ///     Gets or sets the optional publication lease owner.
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    ///     Gets or sets the optional UTC timestamp when the publication lease expires.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>
    ///     Gets or sets the optional latest publication error.
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
