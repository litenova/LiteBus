namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     The Object Lock retention mode applied to a published checkpoint object.
/// </summary>
/// <remarks>
///     The mode travels on each write together with the retain-until date counted from the retention period, so every
///     published object carries its own protection. The bucket must have Object Lock enabled.
/// </remarks>
public enum S3CheckpointRetentionMode
{
    /// <summary>
    ///     Governance retention: the object cannot be overwritten or deleted before expiry without a privileged
    ///     bypass, so operators stay able to remove a checkpoint written in error.
    /// </summary>
    Governance = 0,

    /// <summary>
    ///     Compliance retention: the object cannot be overwritten or deleted by any identity before expiry, including
    ///     the bucket owner.
    /// </summary>
    Compliance = 1,
}
