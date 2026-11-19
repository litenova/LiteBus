namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Why an audit checkpoint was cut.
/// </summary>
public enum AuditCheckpointKind
{
    /// <summary>
    ///     A checkpoint the checkpoint policy or an explicit request cut while the segment stays open.
    /// </summary>
    Regular = 0,

    /// <summary>
    ///     The last checkpoint of a segment, cut before the segment expires and its documents are deleted.
    /// </summary>
    Final = 1
}
