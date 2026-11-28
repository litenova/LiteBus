namespace LiteBus.Auditing;

/// <summary>
///     States what the registered audit store can do, so composition picks a capture mode the store supports.
/// </summary>
/// <param name="JoinsBusinessTransaction">
///     Whether the audit store can write in the business transaction or session. Composition keeps
///     <c>WithChange</c> capture when this is <see langword="true" /> and picks
///     <c>AfterCommitThroughOutbox</c> when it is <see langword="false" />.
/// </param>
public sealed record AuditStoreCapabilities(bool JoinsBusinessTransaction);
