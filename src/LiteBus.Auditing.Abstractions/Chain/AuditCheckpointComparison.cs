
namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The comparison of an audit chain segment against the checkpoint one source holds.
/// </summary>
/// <param name="SourceName">The name of the checkpoint source compared against.</param>
/// <param name="Checkpoint">
///     The checkpoint the source holds, or <see langword="null" /> when it holds none or could not be reached.
/// </param>
/// <param name="HashMatches">
///     Whether the chain hash matches the checkpoint hash, or <see langword="null" /> when there is no checkpoint to
///     compare.
/// </param>
/// <param name="CountMatches">
///     Whether the chain still holds every document the checkpoint counted, or <see langword="null" /> when there is no
///     checkpoint to compare.
/// </param>
/// <param name="SignatureValid">
///     Whether the checkpoint signatures verify, or <see langword="null" /> when there is no checkpoint or no signer is
///     configured.
/// </param>
public sealed record AuditCheckpointComparison(
    string SourceName,
    AuditCheckpoint? Checkpoint,
    bool? HashMatches,
    bool? CountMatches,
    bool? SignatureValid)
{
    /// <summary>
    ///     Gets a value indicating whether the source could not be reached.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when the source was unreachable. An unreachable source is a warning, not a break:
    ///     the verification stays intact but the comparison records the gap, separate from a source that disagrees.
    /// </value>
    public bool SourceUnreachable { get; init; }
}
