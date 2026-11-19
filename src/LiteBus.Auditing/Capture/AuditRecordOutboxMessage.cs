using LiteBus.Messaging.Abstractions;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Carries one audit record through the transactional outbox to the audit store.
/// </summary>
/// <param name="Record">The audit record delivered to the audit store after commit.</param>
/// <remarks>
///     <para>
///         Under <see cref="AuditCapture.AfterCommitThroughOutbox" />, a success record is enqueued in the business
///         transaction and delivered after commit, at least once. The audit store holds one document per record id,
///         so a redelivered record overwrites rather than duplicates.
///     </para>
///     <para>
///         The message is exempt from auditing: auditing the delivery would record the trail writing to itself.
///     </para>
/// </remarks>
[MessageContract("litebus.audit-record", 1)]
[AuditExempt("Audit delivery records are infrastructure; auditing them would record the trail writing to itself.")]
public sealed record AuditRecordOutboxMessage(AuditRecord Record);
