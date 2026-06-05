namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes one keyset page request for outbox message queries.
/// </summary>
public sealed record OutboxMessagePageRequest
{
    /// <summary>
    ///     Gets the maximum number of items to return in one page.
    /// </summary>
    /// <value>The page size. Defaults to 50.</value>
    public int PageSize { get; init; } = 50;

    /// <summary>
    ///     Gets the opaque cursor returned by a previous page, or <see langword="null" /> for the first page.
    /// </summary>
    public string? Cursor { get; init; }
}
