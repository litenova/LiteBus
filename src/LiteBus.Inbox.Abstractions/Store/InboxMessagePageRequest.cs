namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes one keyset page request for inbox message queries.
/// </summary>
public sealed record InboxMessagePageRequest
{
    /// <summary>
    ///     Gets the largest page size accepted by inbox message queries.
    /// </summary>
    /// <value>The maximum page size. Requests above this value are rejected.</value>
    public const int MaxPageSize = 1000;

    /// <summary>
    ///     Gets the maximum number of items to return in one page.
    /// </summary>
    /// <value>The page size. Defaults to 50. Must be between 1 and <see cref="MaxPageSize" />.</value>
    public int PageSize { get; init; } = 50;

    /// <summary>
    ///     Gets the opaque cursor returned by a previous page, or <see langword="null" /> for the first page.
    /// </summary>
    public string? Cursor { get; init; }
}
