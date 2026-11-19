using System;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Connection and layout settings for the Azure Blob checkpoint anchor.
/// </summary>
/// <remarks>
///     The container, its redundancy, and any container-level retention lock are deployment concerns owned by the
///     application and its infrastructure. This package owns only the publisher that writes checkpoint blobs and the
///     source that reads them back. The application sets these values through the configuration action on
///     <c>AuditHashChainBuilder.PublishCheckpointsToAzureBlob</c>, which validates them before the anchor registers.
/// </remarks>
public sealed record AzureBlobCheckpointAnchorOptions
{
    /// <summary>
    ///     Gets or sets the Azure Storage connection string used to create the shared blob service client.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    ///     Gets or sets the blob container holding checkpoint blobs. It is created with the first published checkpoint.
    /// </summary>
    public required string ContainerName { get; set; }

    /// <summary>
    ///     Gets or sets the blob name prefix checkpoint blobs are written under.
    /// </summary>
    /// <value>
    ///     The prefix with a trailing slash appended when missing. Empty by default, which writes checkpoints at the
    ///     container root.
    /// </value>
    public string BlobNamePrefix { get; set; } = "";

    /// <summary>
    ///     Gets or sets how long an unlocked immutability policy protects a checkpoint blob after it is written.
    /// </summary>
    /// <value>
    ///     The retention counted from each publish, or <see langword="null" /> when blobs carry no immutability
    ///     policy. The default is <see langword="null" />.
    /// </value>
    /// <remarks>
    ///     The policy is always unlocked, so its expiry can still be extended. A locked policy cannot be shortened or
    ///     removed, which belongs to container-level compliance configuration, not to a per-checkpoint publisher.
    ///     Azurite does not implement immutability policies, so leave this unset in tests that run against it.
    /// </remarks>
    public TimeSpan? ImmutabilityRetention { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a legal hold protects a checkpoint blob after it is written.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when every published blob carries a legal hold until it is explicitly cleared;
    ///     otherwise, <see langword="false" />. The default is <see langword="false" />.
    /// </value>
    /// <remarks>
    ///     Azurite does not implement legal holds, so leave this unset in tests that run against it.
    /// </remarks>
    public bool LegalHold { get; set; }
}
