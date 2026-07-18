using LiteBus.Outbox.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Binds outbox message purge filter values from the HTTP query string.
/// </summary>
public sealed class OutboxMessagePurgeBinding
{
    /// <summary>
    ///     Gets the message identifier that must match when set.
    /// </summary>
    [FromQuery]
    public Guid? MessageId { get; init; }

    /// <summary>
    ///     Gets the message identifiers that must match when set.
    /// </summary>
    [FromQuery]
    public Guid[]? MessageIds { get; init; }

    /// <summary>
    ///     Gets the statuses that must match when set.
    /// </summary>
    [FromQuery]
    public OutboxStatus[]? Statuses { get; init; }

    /// <summary>
    ///     Gets the contract name that must match when set.
    /// </summary>
    [FromQuery]
    public string? ContractName { get; init; }

    /// <summary>
    ///     Gets the topic that must match when set.
    /// </summary>
    [FromQuery]
    public string? Topic { get; init; }

    /// <summary>
    ///     Gets the correlation identifier that must match when set.
    /// </summary>
    [FromQuery]
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the causation identifier that must match when set.
    /// </summary>
    [FromQuery]
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the tenant identifier that must match when set.
    /// </summary>
    [FromQuery]
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets the earliest inclusive created timestamp when set.
    /// </summary>
    [FromQuery]
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>
    ///     Gets the latest inclusive created timestamp when set.
    /// </summary>
    [FromQuery]
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>
    ///     Converts the bound query values to an <see cref="OutboxMessageFilter" />.
    /// </summary>
    /// <returns>The filter used by outbox purge operations.</returns>
    public OutboxMessageFilter ToFilter()
    {
        return new OutboxMessageFilter
        {
            MessageId = MessageId,
            MessageIds = MessageIds,
            Statuses = Statuses,
            ContractName = ContractName,
            Topic = Topic,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            TenantId = TenantId,
            CreatedAfter = CreatedAfter,
            CreatedBefore = CreatedBefore
        };
    }
}