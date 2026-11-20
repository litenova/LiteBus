
namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     One problem found while verifying an audit chain segment.
/// </summary>
/// <param name="Sequence">The sequence at which the problem was found.</param>
/// <param name="Kind">What is wrong.</param>
/// <param name="Detail">A sentence describing the problem for an operator.</param>
public sealed record AuditChainBreak(long Sequence, AuditChainBreakKind Kind, string Detail);
