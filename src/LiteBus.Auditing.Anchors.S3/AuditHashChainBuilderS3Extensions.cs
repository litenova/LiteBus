using System;
using LiteBus.Auditing;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Anchors audit chain checkpoints in S3-compatible object storage through <see cref="AuditHashChainBuilder" />.
/// </summary>
public static class AuditHashChainBuilderS3Extensions
{
    /// <summary>
    ///     Publishes every cut checkpoint to S3-compatible object storage and verifies segments against the stored copy.
    /// </summary>
    /// <param name="builder">The audit hash chain builder.</param>
    /// <param name="configure">The anchor settings configuration action.</param>
    /// <returns>The audit hash chain builder for chaining.</returns>
    /// <remarks>
    ///     This validates the settings, records the anchor publisher and source types, and registers
    ///     <see cref="S3CheckpointAnchorModule" /> as a child of the auditing module, so the container constructs
    ///     the publisher and source with their settings and client. Calling this twice registers the anchor once;
    ///     the repeated call keeps the first settings.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">The configured settings fail anchor validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The configured retention is not defined or not positive.</exception>
    public static AuditHashChainBuilder PublishCheckpointsToS3(
        this AuditHashChainBuilder builder,
        Action<S3CheckpointAnchorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new S3CheckpointAnchorOptions { BucketName = "" };
        configure(options);
        S3CheckpointAnchorOptionsValidator.Validate(options);

        foreach (var childModule in builder.Owner.CollectSubModules())
        {
            if (childModule is S3CheckpointAnchorModule)
            {
                return builder;
            }
        }

        builder.PublishCheckpointsTo<S3CheckpointPublisher>();
        builder.VerifyAgainst<S3CheckpointSource>();
        builder.Owner.RegisterChildModule(new S3CheckpointAnchorModule(options));

        return builder;
    }
}
