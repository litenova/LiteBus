using System;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Asks an <see cref="IAuditChain" /> to verify one segment.
/// </summary>
/// <param name="Segment">The segment to verify.</param>
/// <param name="FromSequence">
///     Only records after this sequence are checked, trusting the checkpoint at it, or <see langword="null" /> to
///     verify the whole segment.
/// </param>
/// <param name="UnlinkedOlderThan">
///     Report an <see cref="AuditChainBreakKind.Unlinked" /> break for records unlinked longer than this, or
///     <see langword="null" /> to use the configured threshold.
/// </param>
/// <param name="CheckpointStaleAfter">
///     Report an <see cref="AuditChainBreakKind.CheckpointStale" /> break when the last checkpoint is older than this
///     while records exist after it, or <see langword="null" /> to use the configured threshold.
/// </param>
public sealed record AuditChainVerificationRequest(
    string Segment,
    long? FromSequence = null,
    TimeSpan? UnlinkedOlderThan = null,
    TimeSpan? CheckpointStaleAfter = null);
