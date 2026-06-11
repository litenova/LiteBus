using System.Collections.Generic;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one page of outbox messages returned by <see cref="IOutboxMessageQuery.QueryAsync" />.
/// </summary>
/// <param name="Items">The envelopes in this page.</param>
/// <param name="HasMore">Whether another page exists after this one.</param>
/// <param name="NextCursor">
///     The cursor to pass on the next request when <paramref name="HasMore" /> is
///     <see langword="true" />.
/// </param>
public sealed record OutboxMessagePage(
    IReadOnlyList<OutboxEnvelope> Items,
    bool HasMore,
    string? NextCursor);