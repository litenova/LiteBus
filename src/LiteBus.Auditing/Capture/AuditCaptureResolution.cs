using System;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     The capture mode one message is captured with, after its declaration and the audit store capabilities apply.
/// </summary>
/// <param name="Requested">
///     The capture mode the auditing builder set, or the per-message override when the message declares one.
/// </param>
/// <param name="Effective">
///     The capture mode records are captured with. It matches <paramref name="Requested" /> unless the audit store
///     cannot join the business transaction, in which case it is
///     <see cref="AuditCapture.AfterCommitThroughOutbox" />.
/// </param>
/// <param name="OverridingMessageType">
///     The message whose declaration overrode the builder default, or <see langword="null" /> when no message
///     declares an override.
/// </param>
/// <param name="OutboxSelectedBecauseStoreCannotJoinTransaction">
///     <see langword="true" /> when composition set the requested WithChange mode aside and selected
///     <see cref="AuditCapture.AfterCommitThroughOutbox" /> because the audit store cannot join the business
///     transaction. The choice is reported in the composition model rather than applied silently.
/// </param>
internal sealed record AuditCaptureResolution(
    AuditCapture Requested,
    AuditCapture Effective,
    Type? OverridingMessageType,
    bool OutboxSelectedBecauseStoreCannotJoinTransaction);
