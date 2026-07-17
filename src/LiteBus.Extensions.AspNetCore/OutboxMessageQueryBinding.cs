using LiteBus.Outbox.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Binds outbox message filter and pagination values from the HTTP query string.
/// </summary>
public sealed class OutboxMessageQueryBinding
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
    ///     Gets the maximum number of items to return in one page.
    /// </summary>
    [FromQuery]
    public int PageSize { get; init; } = 50;

    /// <summary>
    ///     Gets the opaque cursor returned by a previous page.
    /// </summary>
    [FromQuery]
    public string? Cursor { get; init; }

    /// <summary>
    ///     Converts the bound query values to an <see cref="OutboxMessageFilter" />.
    /// </summary>
    /// <returns>The filter used by outbox store queries.</returns>
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

    /// <summary>
    ///     Converts the bound pagination values to an <see cref="OutboxMessagePageRequest" />.
    /// </summary>
    /// <param name="maxPageSize">The largest page size accepted by the management endpoint.</param>
    /// <returns>The page request used by outbox store queries.</returns>
    public OutboxMessagePageRequest ToPageRequest(int maxPageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageSize);

        if (PageSize <= 0 || PageSize > maxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPageSize), PageSize, $"PageSize must be between 1 and {maxPageSize}.");
        }

        return new OutboxMessagePageRequest
        {
            PageSize = PageSize,
            Cursor = Cursor
        };
    }
}
