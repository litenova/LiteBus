using System.Collections.Generic;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents one page of inbox messages returned by <see cref="IInboxMessageQuery.QueryAsync" />.
/// </summary>
/// <param name="Items">The envelopes in this page.</param>
/// <param name="HasMore">Whether another page exists after this one.</param>
/// <param name="NextCursor">The cursor to pass on the next request when <paramref name="HasMore" /> is <see langword="true" />.</param>
public sealed record InboxMessagePage(
    IReadOnlyList<InboxEnvelope> Items,
    bool HasMore,
    string? NextCursor);
