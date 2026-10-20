namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Composite outbox store role used by operator tooling to query, replay, purge, and inspect stored messages.
/// </summary>
/// <remarks>
///     Combines dead-letter, retention, diagnostics, query, and purge store roles. Fine-grained interfaces remain
///     available for callers that depend on a single concern.
/// </remarks>
public interface IOutboxOperationsStore :
    IOutboxDeadLetterStore,
    IOutboxRetentionStore,
    IOutboxDiagnosticsStore,
    IOutboxMessageQuery,
    IOutboxPurgeStore
{
}
