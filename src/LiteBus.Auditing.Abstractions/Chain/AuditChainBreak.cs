using System;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     One problem found while verifying an audit chain segment.
/// </summary>
/// <param name="Sequence">The sequence at which the problem was found.</param>
/// <param name="Kind">What is wrong.</param>
/// <param name="Detail">A sentence describing the problem for an operator.</param>
public sealed record AuditChainBreak(long Sequence, AuditChainBreakKind Kind, string Detail)
{
    /// <summary>
    ///     Gets the question the break answers.
    /// </summary>
    /// <value>
    ///     <see cref="AuditChainBreakCategory.Coverage" /> when a checkpoint, publication, or link has not happened or
    ///     stopped happening, and <see cref="AuditChainBreakCategory.Integrity" /> when the chain or a checkpoint was
    ///     altered or does not match.
    /// </value>
    public AuditChainBreakCategory Category => Kind switch
    {
        AuditChainBreakKind.CheckpointMissing => AuditChainBreakCategory.Coverage,
        AuditChainBreakKind.CheckpointStale => AuditChainBreakCategory.Coverage,
        AuditChainBreakKind.CheckpointUnpublished => AuditChainBreakCategory.Coverage,
        AuditChainBreakKind.Unlinked => AuditChainBreakCategory.Coverage,
        AuditChainBreakKind.Gap => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.Duplicate => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.PreviousHashMismatch => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.HashMismatch => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.UnknownHashScheme => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.CheckpointHashMismatch => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.CheckpointCountMismatch => AuditChainBreakCategory.Integrity,
        AuditChainBreakKind.CheckpointSignatureInvalid => AuditChainBreakCategory.Integrity,
        _ => throw new InvalidOperationException($"The audit chain break kind '{Kind}' has no category."),
    };
}
