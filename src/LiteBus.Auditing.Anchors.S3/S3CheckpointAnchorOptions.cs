using System;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Connection and layout settings for the S3 checkpoint anchor.
/// </summary>
/// <remarks>
///     The bucket, its region, and any bucket-level retention lock are deployment concerns owned by the
///     application and its infrastructure. This package owns only the publisher that writes checkpoint objects and the
///     source that reads them back. The application sets these values through the configuration action on
///     <c>AuditHashChainBuilder.PublishCheckpointsToS3</c>, which validates them before the anchor registers.
/// </remarks>
public sealed record S3CheckpointAnchorOptions
{
    /// <summary>
    ///     Gets or sets the bucket holding checkpoint objects. It is created with the first published checkpoint.
    /// </summary>
    public required string BucketName { get; set; }

    /// <summary>
    ///     Gets or sets the object key prefix checkpoint objects are written under.
    /// </summary>
    /// <value>
    ///     The prefix with a trailing slash appended when missing. Empty by default, which writes checkpoints at the
    ///     bucket root.
    /// </value>
    public string KeyPrefix { get; set; } = "";

    /// <summary>
    ///     Gets or sets the optional AWS region system name such as <c>us-east-1</c>.
    /// </summary>
    /// <remarks>
    ///     When both <see cref="Region" /> and <see cref="ServiceUrl" /> are <see langword="null" />, the AWS SDK
    ///     resolves the region from environment variables, the shared configuration file, or instance metadata.
    ///     When <see cref="ServiceUrl" /> is set, the service URL takes precedence and no AWS region resolution
    ///     is required.
    /// </remarks>
    public string? Region { get; set; }

    /// <summary>
    ///     Gets or sets the optional service URL used for S3-compatible stores such as MinIO or custom endpoints.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    ///     Gets or sets the optional access key used when explicit credentials are required.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    ///     Gets or sets the optional secret key used when explicit credentials are required.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether object requests use path-style addressing.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when keys are addressed as a path below the endpoint, which most S3-compatible
    ///     stores require; otherwise, <see langword="false" /> for virtual-hosted style. The default is
    ///     <see langword="false" />.
    /// </value>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    ///     Gets or sets the Object Lock retention mode applied to every published checkpoint object.
    /// </summary>
    /// <value>
    ///     The retention mode, or <see langword="null" /> when published objects carry no retention. The default is
    ///     <see langword="null" />.
    /// </value>
    /// <remarks>
    ///     Retention requires a bucket with Object Lock enabled. Buckets this package creates carry Object Lock when
    ///     a mode is configured; a bucket provisioned without it rejects retained writes.
    /// </remarks>
    public S3CheckpointRetentionMode? RetentionMode { get; set; }

    /// <summary>
    ///     Gets or sets how long Object Lock retention protects a checkpoint object after it is written.
    /// </summary>
    /// <value>
    ///     The retention counted from each publish, or <see langword="null" /> when published objects carry no
    ///     retention. The default is <see langword="null" />.
    /// </value>
    public TimeSpan? RetentionPeriod { get; set; }
}
