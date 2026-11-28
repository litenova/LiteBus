using System;
using LiteBus.Auditing;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Anchors audit chain checkpoints in Azure Blob Storage through <see cref="AuditHashChainBuilder" />.
/// </summary>
public static class AuditHashChainBuilderAzureBlobExtensions
{
    /// <summary>
    ///     Publishes every cut checkpoint to Azure Blob Storage and verifies segments against the stored copy.
    /// </summary>
    /// <param name="builder">The audit hash chain builder.</param>
    /// <param name="configure">The anchor settings configuration action.</param>
    /// <returns>The audit hash chain builder for chaining.</returns>
    /// <remarks>
    ///     This validates the settings, records the anchor publisher and source types, and registers
    ///     <see cref="AzureBlobCheckpointAnchorModule" /> as a child of the auditing module, so the container
    ///     constructs the publisher and source with their settings and client. Calling this twice registers the
    ///     anchor once; the repeated call keeps the first settings.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">The configured settings fail anchor validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The configured immutability retention is not positive.</exception>
    public static AuditHashChainBuilder PublishCheckpointsToAzureBlob(
        this AuditHashChainBuilder builder,
        Action<AzureBlobCheckpointAnchorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AzureBlobCheckpointAnchorOptions { ConnectionString = "", ContainerName = "" };
        configure(options);
        AzureBlobCheckpointAnchorOptionsValidator.Validate(options);

        foreach (var childModule in builder.Owner.CollectSubModules())
        {
            if (childModule is AzureBlobCheckpointAnchorModule)
            {
                return builder;
            }
        }

        builder.PublishCheckpointsTo<AzureBlobCheckpointPublisher>();
        builder.VerifyAgainst<AzureBlobCheckpointSource>();
        builder.Owner.RegisterChildModule(new AzureBlobCheckpointAnchorModule(options));

        return builder;
    }
}
